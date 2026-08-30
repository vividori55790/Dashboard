using TelemetryDashboard.Core.Query;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// Answers <c>/metrics</c>: this host's live state in Prometheus text exposition format.
/// </summary>
/// <remarks>
/// The point of the format is that nothing here has to be integrated with. Grafana, Prometheus,
/// VictoriaMetrics, Telegraf and a Datadog agent all read it already, and none of them could read
/// anything this hub served before -- an MQTT relay and Slack alerts push somewhere specific, and a
/// monitoring system that already exists wants to pull.
///
/// <para>
/// <b>The rule this endpoint exists to keep.</b> A channel with no sample, a limit that has
/// evaluated nothing, an offset with no error bar and a node never heard from are all <b>absent</b>
/// here, never zero. This is the boundary where that is easiest to get wrong, because the format
/// has no null: a field that <c>/api/status</c> can send as <c>null</c> has no spelling here except
/// omission. And the consequence is worse than a misleading dashboard. Prometheus interpolates
/// between points and alerting rules fire on values, so a zero exported to mean "not measured"
/// becomes a confident reading inside somebody else's alert -- this product's central failure,
/// exported into a system that has no way to know it was a placeholder.
/// </para>
/// <para>
/// The format agrees, which is the useful part: OpenMetrics says of a series that stops existing
/// that "there is no special marker or signal for this situation -- subsequent expositions simply
/// do not include this Metric", and Prometheus then marks it stale and returns nothing for it. So
/// omission is not this endpoint inventing a convention; it is the one the scraper already speaks.
/// </para>
/// <para>
/// <b>Naming.</b> Taken from the Prometheus naming conventions rather than from this codebase's
/// internal names: <c>snake_case</c>, an application prefix on everything, a <c>_total</c> suffix
/// only on a monotonic counter, base units in the name (<c>_seconds</c>, never milliseconds), and
/// no label name spelled into a metric name -- a channel is a <c>channel</c> label, not part of the
/// metric. Where the two conflict, the convention wins: <c>duplicatesRefused</c> on
/// <c>/api/status</c> is <c>telemetry_exchange_duplicates_refused_total</c> here.
/// </para>
/// <para>
/// <b>Product decision, not taken.</b> The <see cref="Prefix"/> is a one-way door. It becomes part
/// of the contract the moment anyone writes an alert rule or a dashboard against it, and changing
/// it afterwards breaks their queries silently -- the series just stop existing. <c>telemetry_</c>
/// reads best and is the most likely to collide with another exporter on the same host;
/// <c>telemetry_host_</c> or a site-chosen namespace would not. That belongs to whoever owns the
/// product's public surface, and until they choose it, this is the default rather than the answer.
/// </para>
/// </remarks>
public static partial class MetricsEndpoint
{
    /// <summary>The path this is served at.</summary>
    /// <remarks>
    /// The conventional one. Every scraper defaults to it, so an operator who has to configure a
    /// path has already been given a reason to think this endpoint is unusual.
    /// </remarks>
    public const string Path = "/metrics";

    /// <summary>
    /// The content type of the reply, version parameter included.
    /// </summary>
    /// <remarks>
    /// A missing version defaults to the most recent, which would make this document's meaning
    /// depend on the scraper's build rather than on what was written. Stating 0.0.4 pins it.
    /// </remarks>
    public const string ContentType = "text/plain; version=0.0.4; charset=utf-8";

    /// <summary>The namespace every metric here carries. See the type remarks.</summary>
    public const string Prefix = "telemetry_";

    /// <summary>Renders the whole document.</summary>
    /// <remarks>
    /// A snapshot taken family by family rather than under one lock, so the counters in it can
    /// disagree by a sample or two. That is the right trade: locking the ingest path for the
    /// duration of a scrape would make an operator's monitoring system a source of jitter on the
    /// plant floor, and no consumer of a counter can tell a one-sample skew from scrape timing
    /// anyway.
    /// <para>
    /// Host counters first, then channels, because the channel list is the unbounded part and a
    /// reader tailing the response should reach the summary without paging through it.
    /// </para>
    /// </remarks>
    public static string Render(TelemetryStreamingServer server)
    {
        var document = new Document();

        WriteHost(document, server);
        WriteFleet(document, server);
        WriteLimits(document, server);
        WriteChannels(document, server, SeriesClock.UtcNowSec());

        return document.ToString();
    }
}
