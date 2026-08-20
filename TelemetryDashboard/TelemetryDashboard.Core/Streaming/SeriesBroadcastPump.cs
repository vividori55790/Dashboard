using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Query;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// Serves each subscribed client a reduced frame of the channels it asked for, at the rate it
/// asked for.
/// </summary>
/// <remarks>
/// This is the half of the fix that ingest cannot do. Reduction alone still costs one fan-out per
/// sample; a client that asked for 10 Hz is served ten times a second regardless of whether ten
/// samples or ten million arrived in between. The cost of a viewer is therefore set by the
/// viewer's screen, not by the plant's sample rate.
/// </remarks>
public sealed class SeriesBroadcastPump : IAsyncDisposable
{
    private readonly TelemetryBroadcastHub _hub;
    private readonly SeriesQueryService _query;
    private readonly CancellationTokenSource _cts = new();

    private Task? _loop;
    private long _framesSent;
    private long _pointsSent;

    public SeriesBroadcastPump(TelemetryBroadcastHub hub, SeriesQueryService query)
    {
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _query = query ?? throw new ArgumentNullException(nameof(query));
    }

    /// <summary>
    /// How often the pump looks for due subscriptions. It is not the delivery rate: a client's
    /// own <see cref="SubscriptionOptions.MaxUpdateHz"/> decides that.
    /// </summary>
    public TimeSpan TickInterval { get; init; } = TimeSpan.FromMilliseconds(10);

    public bool IsRunning { get; private set; }

    /// <summary>Reduced frames delivered across all subscribers.</summary>
    public long FramesSent => Interlocked.Read(ref _framesSent);

    /// <summary>Points delivered across all subscribers, the wire cost of the display path.</summary>
    public long PointsSent => Interlocked.Read(ref _pointsSent);

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await PumpOnceAsync(SeriesClock.UtcNowSec(), token).ConfigureAwait(false);
                await Task.Delay(TickInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // One malformed subscription must not stop every other client's feed. The hub
                // evicts transports that fail; anything else is retried on the next tick.
                await Task.Delay(TickInterval, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Runs one pass: every subscription whose interval has elapsed gets exactly one frame.
    /// </summary>
    /// <remarks>Public so a test can drive the pump on its own clock instead of on wall time.</remarks>
    public async Task PumpOnceAsync(double nowSec, CancellationToken cancellationToken = default)
    {
        List<Task>? deliveries = null;

        foreach ((ITelemetrySubscriber subscriber, TelemetrySubscription subscription) in _hub.SubscribedClients())
        {
            if (subscription.Options.Channels.Count == 0) continue;
            if (!subscription.TryClaimSendSlot(nowSec)) continue;

            deliveries ??= new List<Task>();
            deliveries.Add(SendFrameAsync(subscriber, subscription, nowSec, cancellationToken));
        }

        if (deliveries is not null) await Task.WhenAll(deliveries).ConfigureAwait(false);
    }

    private async Task SendFrameAsync(
        ITelemetrySubscriber subscriber,
        TelemetrySubscription subscription,
        double nowSec,
        CancellationToken cancellationToken)
    {
        SubscriptionOptions options = subscription.Options;

        SeriesQueryResult result = _query.Execute(SeriesQueryRequest.Recent(
            options.Channels, options.WindowSec, options.MaxPoints, nowSec, options.Method));

        await _hub.SendToAsync(subscriber, SeriesFrameWriter.Write(result, nowSec), cancellationToken)
                  .ConfigureAwait(false);

        subscription.MarkSent();
        Interlocked.Increment(ref _framesSent);
        Interlocked.Add(ref _pointsSent, result.ReturnedPointCount);
    }

    public async ValueTask DisposeAsync()
    {
        IsRunning = false;
        _cts.Cancel();

        if (_loop is not null)
        {
            await Task.WhenAny(_loop, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
        }

        _cts.Dispose();
    }
}
