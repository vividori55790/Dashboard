using System;

namespace TelemetryDashboard.Core.Services;

/// <summary>
/// What the generated panels ask Prometheus for, and which datasource they ask.
/// </summary>
/// <remarks>
/// Separated from the panel shapes because it is the half with a counterparty. How a line is drawn
/// is this file's business alone; what series it names has to agree with whatever
/// <c>/metrics</c> publishes, and an agreement kept in one place can be checked.
/// </remarks>
public static partial class GrafanaDashboardExport
{
    /// <summary>
    /// The series <c>/metrics</c> is expected to publish, one per reading, labelled by identity.
    /// </summary>
    /// <remarks>
    /// <b>This is a contract with the workstream building <c>/metrics</c> (ROADMAP W2), and it is
    /// the one thing here that will break silently if the two disagree</b> — a dashboard whose
    /// queries name a series nobody exports imports perfectly and then draws nothing, which is the
    /// empty-graph failure arriving by the back door. It is stated in one place, repeated in the
    /// dashboard's own description so an operator can see what is assumed, and reported as needing
    /// a decision rather than settled here.
    /// <para>
    /// One labelled series rather than a metric per channel, for two reasons. Prometheus's naming
    /// conventions say to differentiate with labels and not to encode dimensions in the metric
    /// name. And a channel name here is <c>dab.bus_voltage</c> — a dot is legal in a label value
    /// and not in a classic metric name, so per-channel metric names would mean sanitising, and
    /// sanitising maps <c>a.b</c> and <c>a_b</c> onto one series. That is ARCHITECTURE §2's
    /// collision exactly: one chart alternating between two physical sensors, with nothing in the
    /// numbers to reveal it.
    /// </para>
    /// </remarks>
    public const string MetricName = "telemetry_channel_value";

    private const string DatasourceVariableName = "ds_prometheus";

    /// <summary>The dashboard's datasource picker, resolved by Grafana at import.</summary>
    /// <remarks>
    /// Shape taken from node-exporter-full (grafana.com dashboard 1860). <c>refresh: 1</c> is
    /// "on dashboard load", and an empty <c>current</c> lets Grafana select the instance's own
    /// Prometheus — which is what makes this import with nothing for the operator to type.
    /// <para>
    /// The schemastore <c>grafana-dashboard-5.x</c> schema calls <c>refresh</c> a boolean and
    /// rejects the 1. That schema is the stale one: this field became an enum — 0 never, 1 on
    /// load, 2 on time-range change — and 1860 as exported from Grafana 11.6.1 carries the integer.
    /// Recorded rather than silently overridden, because the next person to run a validator will
    /// see the same complaint.
    /// </para>
    /// </remarks>
    private static object DatasourceVariable() => new
    {
        name = DatasourceVariableName,
        label = "Prometheus",
        type = "datasource",
        query = "prometheus",
        refresh = 1,
        current = new { },
        options = Array.Empty<object>()
    };

    private static object DatasourceRef() => new
    {
        type = "prometheus",
        uid = "${" + DatasourceVariableName + "}"
    };

    /// <summary>The one query a channel panel runs.</summary>
    /// <remarks>
    /// Matched on both labels rather than on the channel alone. ARCHITECTURE §2: a channel name is
    /// only unique within the node that observed it, and a panel selecting <c>channel="TEMP"</c>
    /// across a fleet would plot two machines' sensors as one series.
    /// </remarks>
    private static object[] ChannelTargets(string nodeId, string channel) => new object[]
    {
        new
        {
            refId = "A",

            // No datasource on the target. The panel already declares one and a query inherits it;
            // 1860's targets carry none either, and a second copy of the same reference is a second
            // place for it to be wrong.
            editorMode = "code",
            range = true,
            legendFormat = "{{node}}",
            expr = $"{MetricName}{{node=\"{PromQlLabel(nodeId)}\", "
                 + $"channel=\"{PromQlLabel(channel)}\"}}"
        }
    };

    /// <summary>Escapes a label value for a PromQL matcher.</summary>
    /// <remarks>
    /// A channel name arrives from a device and is not trusted (§7). An unescaped quote in one does
    /// not merely break its own panel — it terminates the matcher and leaves a syntactically valid
    /// query selecting something else entirely.
    /// </remarks>
    private static string PromQlLabel(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);
}
