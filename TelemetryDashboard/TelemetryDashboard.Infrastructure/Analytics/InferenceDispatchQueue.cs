using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TelemetryDashboard.Infrastructure.Analytics;

/// <summary>
/// A bounded hand-off from the ingest thread to a model that may be slow, absent or wrong.
/// </summary>
/// <remarks>
/// The same shape as <c>TelemetryDashboard.Host.Outbound.OutboundQueue</c>, and for the same two
/// reasons: scoring inline would let a stalled model freeze ingest, and an unbounded queue would
/// turn a model outage into a memory leak. It is a separate type rather than that one because
/// Infrastructure cannot reference the host — the pattern travels, the code cannot.
///
/// <para>Full means refuse and count, never overwrite. <c>BoundedChannelFullMode.DropWrite</c>
/// discards an item while returning true from <c>TryWrite</c>, so the drop counter would never
/// fire — silent loss inside the machinery written to make loss visible. That defect was found in
/// the outbound queue by its own tests, and repeating it here would have been free.</para>
/// </remarks>
public sealed class InferenceDispatchQueue : IAsyncDisposable
{
    private readonly Channel<InferenceRequest> _channel;
    private readonly IInferenceEndpoint _endpoint;
    private readonly Action<InferenceRequest, InferenceScore?> _onScored;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pump;
    private readonly InferenceTally _tally;

    /// <param name="capacity">Windows that may be waiting before new ones are refused.</param>
    /// <param name="onScored">
    /// Called on the pump thread with the answer, or with null when none came back. Called for
    /// <em>every</em> request, including the failed ones: a detector that only heard about
    /// successes could not tell "still waiting" from "asked and got nothing".
    /// </param>
    public InferenceDispatchQueue(
        IInferenceEndpoint endpoint,
        int capacity,
        InferenceTally tally,
        Action<InferenceRequest, InferenceScore?> onScored)
    {
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _onScored = onScored ?? throw new ArgumentNullException(nameof(onScored));
        _tally = tally ?? throw new ArgumentNullException(nameof(tally));

        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "A queue that holds nothing can dispatch nothing.");

        _channel = Channel.CreateBounded<InferenceRequest>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        _pump = Task.Run(() => PumpAsync(_cts.Token));
    }

    /// <summary>Offers a window. Never blocks; counts the loss when the model is behind the feed.</summary>
    /// <returns>True when the window was queued, false when it was refused and counted.</returns>
    public bool Offer(InferenceRequest request)
    {
        _tally.CountOffered();

        if (_channel.Writer.TryWrite(request)) return true;

        _tally.CountDropped();
        return false;
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (InferenceRequest request in _channel.Reader
                .ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                InferenceScore? score = null;
                try
                {
                    score = await _endpoint.ScoreAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // The endpoint counts its own failures; this is the backstop for one that
                    // throws where it promised to return null. One bad answer must not stop the pump.
                }

                _onScored(request, score);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    /// <summary>
    /// Stops the pump, waiting briefly for an in-flight request and then giving up.
    /// </summary>
    /// <remarks>
    /// Bounded on purpose. The whole reason this queue exists is that the model may be unresponsive,
    /// so a shutdown that waited for it would turn a slow model into a host that will not exit.
    /// Anything still queued is abandoned: a score for a window from before the shutdown is of no
    /// use to anybody afterwards.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();

        await Task.WhenAny(_pump, Task.Delay(StopGrace)).ConfigureAwait(false);
        _cts.Dispose();
    }

    /// <summary>How long shutdown waits for an in-flight request to notice the cancellation.</summary>
    private static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(2);
}
