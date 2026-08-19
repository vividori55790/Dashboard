using System;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>
/// Background pump that moves packets out of a <see cref="ChannelDataLogger"/> ring and into a
/// durable <see cref="IDataLogger"/> in batches.
/// </summary>
/// <remarks>
/// The ring evicts its oldest entries once full, so anything not drained in time is gone. This is
/// the half of the pipeline that empties it, batching to keep the transaction count down at the
/// ingest rates the ring is sized for.
/// <para>
/// A batch whose write fails is kept and retried on the next pass rather than discarded, and
/// <see cref="WriteFailed"/> reports the cause each time. See <see cref="TelemetryBatchMover"/> for
/// why the batch is never dropped.
/// </para>
/// </remarks>
public sealed class ChannelDataLoggerDrain : IAsyncDisposable
{
    private readonly TelemetryBatchMover _mover;
    private readonly TimeSpan _idleDelay;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private long _failedAttempts;

    /// <summary>Creates a drain from <paramref name="source"/> into <paramref name="sink"/>.</summary>
    /// <param name="source">Ring buffer fed by ingest.</param>
    /// <param name="sink">Durable store receiving the batches.</param>
    /// <param name="batchSize">Maximum packets per transaction.</param>
    /// <param name="idleDelay">Pause taken when the ring did not yield a full batch. Defaults to 50 ms.</param>
    public ChannelDataLoggerDrain(
        ChannelDataLogger source,
        IDataLogger sink,
        int batchSize = 512,
        TimeSpan? idleDelay = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sink);
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be positive.");
        }

        _mover = new TelemetryBatchMover(source, sink, batchSize);
        _idleDelay = idleDelay ?? TimeSpan.FromMilliseconds(50);
    }

    /// <summary>Whether the background loop is running.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Packets committed to the sink over this drain's lifetime.</summary>
    public long DrainedCount => _mover.DrainedCount;

    /// <summary>Batches committed to the sink.</summary>
    public long BatchCount => _mover.BatchCount;

    /// <summary>Write attempts that threw. Non-zero means packets were held back and retried.</summary>
    public long FailedAttempts => Interlocked.Read(ref _failedAttempts);

    /// <summary>
    /// Packets taken from the ring but not yet committed. Approximate while the loop is running.
    /// </summary>
    public int PendingCount => _mover.PendingCount;

    /// <summary>Raised when a batch write throws. The batch is retained and retried.</summary>
    public event EventHandler<Exception>? WriteFailed;

    /// <summary>Starts the background loop. A no-op when already running.</summary>
    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        IsRunning = true;
        _loop = Task.Run(() => RunAsync(token), CancellationToken.None);
    }

    /// <summary>Stops the loop, then writes everything still buffered or still in the ring.</summary>
    /// <remarks>
    /// The final flush runs to completion and its failures propagate. Abandoning the tail of a
    /// recording because the process is shutting down is data loss, and a shutdown path that
    /// swallowed the error is exactly where it would go unnoticed.
    /// </remarks>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            IsRunning = false;
            _cts?.Cancel();

            if (_loop is not null)
            {
                try { await _loop.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }

            _cts?.Dispose();
            _cts = null;
            _loop = null;
        }

        while (await _mover.FlushOnceAsync(cancellationToken).ConfigureAwait(false) > 0)
        {
        }
    }

    /// <summary>Stops the drain, flushing the remainder.</summary>
    /// <remarks>Propagates a failing final flush, for the reason given on <see cref="StopAsync"/>.</remarks>
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            int written = 0;
            try
            {
                written = await _mover.FlushOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failedAttempts);
                WriteFailed?.Invoke(this, ex);
            }

            // A full batch means the ring still has a backlog, so go straight round again.
            if (written >= _mover.BatchSize) continue;

            try { await Task.Delay(_idleDelay, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }
}
