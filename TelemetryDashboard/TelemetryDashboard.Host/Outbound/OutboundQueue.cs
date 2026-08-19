using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TelemetryDashboard.Host.Outbound;

/// <summary>
/// A bounded hand-off from the ingest thread to a slow external service.
/// </summary>
/// <remarks>
/// Two failure modes are being avoided at once. Publishing to a broker or a webhook inline would
/// let a network stall hold up ingest, so the console and the recording would freeze because
/// something unrelated was slow. An unbounded queue would instead turn an outage into a memory
/// leak — the exact trade-off <see cref="TelemetryDashboard.Infrastructure.WebServer.MqttPublisher"/>
/// already makes for its own send queue.
///
/// So the queue is bounded and refuses when full, and every refusal is counted and reported. A
/// dropped sample the operator is told about is a known gap; a dropped sample nobody counts is a
/// dashboard that quietly disagrees with the archive.
/// </remarks>
public sealed class OutboundQueue<T> : IAsyncDisposable
{
    private readonly Channel<T> _channel;
    private readonly Func<T, CancellationToken, Task> _send;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pump;
    private readonly OutboundTally _tally;

    public OutboundQueue(string name, int capacity, Func<T, CancellationToken, Task> send)
    {
        _tally = new OutboundTally(name);
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            // Wait, not DropWrite. DropWrite discards the item and still returns true from
            // TryWrite, so the queue would lose samples while reporting none lost -- the silent
            // loss this whole type exists to prevent. Wait makes TryWrite refuse instead, and a
            // refusal is something that can be counted.
            FullMode = BoundedChannelFullMode.Wait
        });

        _pump = Task.Run(() => PumpAsync(_cts.Token));
    }

    /// <summary>What this relay delivered, refused and lost.</summary>
    public OutboundTally Tally => _tally;

    /// <summary>Items refused because the queue was full.</summary>
    public long Dropped => _tally.Dropped;

    /// <summary>Items the sender accepted.</summary>
    public long Sent => _tally.Sent;

    /// <summary>Items the sender rejected or threw on.</summary>
    public long Failed => _tally.Failed;

    /// <summary>Offers an item. Never blocks; counts the loss when the queue is full.</summary>
    public void Offer(T item)
    {
        if (_channel.Writer.TryWrite(item)) return;
        _tally.CountDropped();
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (T item in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await _send(item, cancellationToken).ConfigureAwait(false);
                    _tally.CountSent();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // One bad delivery must not stop the queue; the tally is the report.
                    _tally.CountFailed();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    /// <summary>One line for the shutdown report, or null when there is nothing to say.</summary>
    public string? Summary() => _tally.Summary();

    /// <summary>True when shutdown gave up waiting for the sender rather than it finishing.</summary>
    public bool AbandonedOnShutdown => _tally.AbandonedOnShutdown;

    /// <summary>
    /// Drains what it can, then stops whether or not the sender cooperates.
    /// </summary>
    /// <remarks>
    /// Both waits are bounded, and the second one is the important one. Cancelling and then
    /// awaiting the pump assumes the send honours the token, and <c>ISlackClient.SendAlertAsync</c>
    /// takes no token at all — so a webhook that had stopped answering would hold the whole process
    /// open at shutdown, turning a slow alert channel into a host that will not exit. A run that
    /// gives up says so instead of hanging.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();

        // The last alert before a shutdown is usually the one worth having, so drain first.
        await Task.WhenAny(_pump, Task.Delay(DrainGrace)).ConfigureAwait(false);

        _cts.Cancel();

        Task finished = await Task.WhenAny(_pump, Task.Delay(StopGrace)).ConfigureAwait(false);
        if (finished != _pump)
        {
            _tally.AbandonedOnShutdown = true;
        }
        else
        {
            try { await _pump.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }

        _cts.Dispose();
    }

    /// <summary>How long a shutdown waits for queued items to be delivered.</summary>
    private static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(3);

    /// <summary>How long it then waits for an in-flight send to notice the cancellation.</summary>
    private static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(2);
}
