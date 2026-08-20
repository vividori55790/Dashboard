using System;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Query;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// The screen-shaped half of the streaming server: the rolling store, the query API over it, and
/// the pump that serves each client only what it subscribed to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <see cref="BroadcastTelemetry"/> serialises one sample to JSON and fans
/// it out to every connected browser — about 220 bytes on the wire per sample. A million samples a
/// second is then roughly 220 MB/s <em>per subscriber</em>, to draw a chart that can resolve about
/// two thousand points. The browser paid to receive several hundred times more data than it can
/// display, and then reduced it itself, badly.
/// </para>
/// <para>
/// <see cref="PublishSample"/> is the path that scales: two doubles into a ring buffer, no
/// serialisation, no fan-out, cost independent of how many browsers are connected. Viewers are
/// then served from the store at their own rate.
/// </para>
/// </remarks>
public partial class TelemetryStreamingServer
{
    private SeriesBroadcastPump? _pump;

    /// <summary>Rolling per-channel history the query API and the subscription pump read.</summary>
    public SeriesStore Series { get; } = new();

    /// <summary>Screen-shaped queries over <see cref="Series"/>.</summary>
    public SeriesQueryService SeriesQuery { get; }

    /// <summary>Reduced frames delivered to subscribed clients.</summary>
    public long ReducedFramesSent => _pump?.FramesSent ?? 0;

    /// <summary>Points delivered to subscribed clients — the real wire cost of the display path.</summary>
    public long ReducedPointsSent => _pump?.PointsSent ?? 0;

    /// <summary>Clients being served a reduced feed of the channels they asked for.</summary>
    public int SubscribedClientCount => _hub.SubscriptionCount;

    /// <summary>
    /// Records one sample without serialising or broadcasting anything.
    /// </summary>
    /// <param name="channel">Channel identifier, e.g. <c>NODE_7.temp</c>.</param>
    /// <param name="value">The measured value.</param>
    /// <param name="timestampSec">
    /// When it was measured, in Unix epoch seconds. Pass the sample's own timestamp; omitting it
    /// stamps the sample with its arrival time, which is a different measurement and is only
    /// correct when the producer supplied none.
    /// </param>
    public void PublishSample(string channel, double value, double? timestampSec = null) =>
        Series.Append(channel, value, timestampSec ?? SeriesClock.UtcNowSec());

    /// <summary>Runs one query against the rolling store.</summary>
    public SeriesQueryResult Query(SeriesQueryRequest request) => SeriesQuery.Execute(request);

    /// <summary>
    /// Applies a subscription message from a client, or reports that it was not one.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the message was a subscription command and has been handled, so the
    /// application command path never sees it.
    /// </returns>
    internal bool TryApplySubscription(string subscriberId, string? message)
    {
        SubscriptionCommandKind kind = SubscriptionRequestParser.Parse(message, out SubscriptionOptions? options);

        switch (kind)
        {
            case SubscriptionCommandKind.Subscribe when options is not null:
                _hub.Subscribe(subscriberId, options, SeriesClock.UtcNowSec());
                return true;

            case SubscriptionCommandKind.Unsubscribe:
                _hub.Unsubscribe(subscriberId);
                return true;

            default:
                return false;
        }
    }

    private void StartPump()
    {
        _pump = new SeriesBroadcastPump(_hub, SeriesQuery);
        _pump.Start();
    }

    private async Task StopPumpAsync()
    {
        if (_pump is null) return;
        await _pump.DisposeAsync().ConfigureAwait(false);
        _pump = null;
    }
}
