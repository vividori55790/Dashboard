using System.Text.Json;
using System.Text.RegularExpressions;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The generated dashboard's queries match what this host actually exposes.
/// </summary>
/// <remarks>
/// The seam between two pieces of work that were each correct on their own. The exporter generated
/// <c>telemetry_channel_value{node="RIG-01", channel="bus_voltage"}</c>; the endpoint emitted
/// <c>telemetry_channel_value{channel="RIG-01.bus_voltage"}</c> — the node was not a label at all.
/// Every panel imported into Grafana without complaint and drew nothing.
/// <para>
/// Nothing either side could test alone would have caught it. The exporter's tests assert it
/// generates the query it means to; the endpoint's assert it exposes the series it means to; both
/// pass while the two mean different things. This asserts them against each other, which is the
/// only place the disagreement exists.
/// </para>
/// <para>
/// It is a real contract rather than an internal one: the moment an operator imports the dashboard,
/// the metric name and its labels are a thing their queries depend on. Breaking it later is silent
/// on both ends — the endpoint keeps serving and the dashboard keeps loading.
/// </para>
/// </remarks>
public class GrafanaScrapeContractTests
{
    private static readonly Regex Series = new(
        @"^telemetry_channel_value\{(?<labels>[^}]*)\}", RegexOptions.Multiline);

    private static TelemetryStreamingServer HostWithChannels()
    {
        var server = new TelemetryStreamingServer(port: 0);
        var inventory = new Core.Ingest.InputInventory();

        foreach ((string node, string variable, string unit, double value) in new[]
        {
            ("RIG-01", "dab.bus_voltage", "V", 401.0),
            ("RIG-01", "ambient.temperature", "Cel", 22.3),
            ("RIG-02", "machine.speed", "rpm", 1118.0)
        })
        {
            inventory.Observe(
                new Core.Models.RawPacket("COM7", "{}", DateTime.UtcNow),
                new Core.Models.TelemetryPacket(node, variable, value, unit));
            server.PublishSample($"{node}.{variable}", value);
        }

        server.Inputs = inventory;
        return server;
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void EveryGeneratedQueryMatchesASeriesTheEndpointActuallyExposes()
    {
        TelemetryStreamingServer server = HostWithChannels();

        string exposition = MetricsEndpoint.Render(server);
        var exposed = Series.Matches(exposition)
            .Select(m => Labels(m.Groups["labels"].Value))
            .ToList();

        exposed.Should().NotBeEmpty(
            "an endpoint exposing nothing would make every assertion below vacuous");

        string dashboard = JsonSerializer.Serialize(
            GrafanaDashboardExport.Build(server.Inputs, DateTimeOffset.UtcNow));
        string[] queries = QueriesIn(dashboard);

        queries.Should().NotBeEmpty("a dashboard with no panels would pass this for the wrong reason");

        foreach (string query in queries)
        {
            Dictionary<string, string> asked = Labels(
                query[(query.IndexOf('{') + 1)..query.LastIndexOf('}')]);

            bool answered = exposed.Any(series =>
                asked.All(pair => series.ContainsKey(pair.Key) && series[pair.Key] == pair.Value));

            answered.Should().BeTrue(
                $"the dashboard asks for {query} and nothing on /metrics answers it. A panel that "
                + "imports cleanly and draws nothing is the worst shape this failure can take: both "
                + "ends look healthy and only the operator sees the empty graph");
        }
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheNodeIsADimensionOnBothSides()
    {
        // Not merely "the strings agree" -- they would agree just as well if both sides glued the
        // node into one label. Prometheus's conventions and ARCHITECTURE §2 both say identity is
        // several parts, and a consumer that cannot group by node has to regex a label value.
        TelemetryStreamingServer server = HostWithChannels();

        Dictionary<string, string> series = Series.Matches(MetricsEndpoint.Render(server))
            .Select(m => Labels(m.Groups["labels"].Value))
            .First();

        series.Should().ContainKey("node");
        series.Should().ContainKey("channel");
        series["node"].Should().NotContain(".", "the node is the part before the first dot, not the whole key");
        series["channel"].Should().NotStartWith(series["node"]);
    }

    private static Dictionary<string, string> Labels(string labels)
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (System.Text.RegularExpressions.Match pair in Regex.Matches(labels, "(?<name>[a-zA-Z_][a-zA-Z0-9_]*)=\"(?<value>[^\"]*)\""))
        {
            parsed[pair.Groups["name"].Value] = pair.Groups["value"].Value;
        }

        return parsed;
    }

    private static string[] QueriesIn(string dashboard)
    {
        using JsonDocument document = JsonDocument.Parse(dashboard);
        var found = new List<string>();
        Walk(document.RootElement, found);
        return found.Where(q => q.Contains('{')).ToArray();
    }

    /// <summary>Finds every <c>expr</c> wherever the dashboard schema happens to put it.</summary>
    /// <remarks>
    /// Walked rather than indexed by path, so a change to the panel layout does not quietly reduce
    /// this to checking nothing.
    /// </remarks>
    private static void Walk(JsonElement element, List<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.NameEquals("expr") && property.Value.ValueKind == JsonValueKind.String)
                    {
                        found.Add(property.Value.GetString() ?? string.Empty);
                    }

                    Walk(property.Value, found);
                }

                break;

            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray()) Walk(item, found);
                break;
        }
    }
}
