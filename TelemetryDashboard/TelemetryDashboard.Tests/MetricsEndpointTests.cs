using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Cluster;
using TelemetryDashboard.Core.Query;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Tests;

/// <summary>
/// <c>/metrics</c> exports what was measured, and stays silent about what was not.
/// </summary>
/// <remarks>
/// The reason this endpoint needs its own suite rather than an assertion or two is that the text
/// exposition format has no null. Everything <c>/api/status</c> can say by sending a null block --
/// no ledger is attached, no offset has an error bar, no sample has arrived -- has exactly one
/// spelling here, which is to omit the line. The failure mode is therefore silent and one keystroke
/// wide: a zero written where a line should have been absent parses, scrapes, graphs, and fires
/// somebody else's alert as a confident reading.
/// <para>
/// Every "absent" test below is paired with a positive control in the same fixture, because an
/// endpoint that returned an empty document would pass all of them.
/// </para>
/// </remarks>
public class MetricsEndpointTests
{
    private const string Channel = "SIM:COM3.dab.bus_voltage";

    private static TelemetryStreamingServer Host() => new(port: 0);

    private static MetricsExpositionParser.Document Read(TelemetryStreamingServer server) =>
        MetricsExpositionParser.Parse(MetricsEndpoint.Render(server));

    /// <summary>Ten samples one second apart, the newest <paramref name="ageSec"/> ago.</summary>
    private static void Fill(SeriesStore store, string channel, double ageSec)
    {
        double newest = SeriesClock.UtcNowSec() - ageSec;
        for (int i = 9; i >= 0; i--) store.Append(channel, 411.5 + i, newest - i);
    }

    // ---- the rule ----------------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void AChannelWithNoSampleIsAbsentRatherThanExportedAsZero()
    {
        // The positive control and the rule in one document, so neither can pass by the endpoint
        // returning nothing at all.
        TelemetryStreamingServer server = Host();
        Fill(server.Series, Channel, ageSec: 0.0);

        MetricsExpositionParser.Document document = Read(server);

        document.Samples.Should().ContainKey($"telemetry_channel_value{{channel=\"{Channel}\"}}");
        document.Samples.Keys
            .Where(key => key.StartsWith("telemetry_channel_", StringComparison.Ordinal))
            .Should().OnlyContain(key => key.Contains(Channel, StringComparison.Ordinal),
                "no channel this host never heard from may appear, and a zero for one would");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AReadingOlderThanItsChannelsOwnCadenceStopsBeingExportedAsCurrent()
    {
        // A one-second channel silent for a thousand seconds. Prometheus stamps a scraped sample
        // with the scrape time, so exporting this value at all asserts it was read just now.
        TelemetryStreamingServer server = Host();
        Fill(server.Series, Channel, ageSec: 1000.0);

        MetricsExpositionParser.Document document = Read(server);

        document.Samples.Should().NotContainKey($"telemetry_channel_value{{channel=\"{Channel}\"}}");
        document.Samples.Should().ContainKey(
            $"telemetry_channel_last_sample_timestamp_seconds{{channel=\"{Channel}\"}}",
            "the channel exists and the silence itself is the measurement worth exporting");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AnOffsetFromASingleObservationCarriesNoSpreadAtAll()
    {
        // ARCHITECTURE §3, at the boundary. One observation supplies an offset and no error bar;
        // a zero spread would be the tightest bound in the fleet, produced by the least evidence
        // available, and CanOrder would be answered "yes" by whoever read it.
        var buffer = new TimeSyncJitterBuffer();
        buffer.SyncNodeClock("PEER-01", masterTime: 100.4, nodeTime: 100.0);

        TelemetryStreamingServer server = Host();
        server.Clocks = buffer.ObservedClocks;

        MetricsExpositionParser.Document document = Read(server);

        document.Samples.Should().ContainKey("telemetry_node_clock_offset_seconds{node=\"PEER-01\"}");
        document.Samples.Should().NotContainKey("telemetry_node_clock_offset_spread_seconds{node=\"PEER-01\"}");

        // The positive control: a second observation makes the bound exist, so the absence above
        // is the estimator's state and not this endpoint dropping the family on the floor.
        buffer.SyncNodeClock("PEER-01", masterTime: 200.9, nodeTime: 200.0);
        Read(server).Samples.Should().ContainKey("telemetry_node_clock_offset_spread_seconds{node=\"PEER-01\"}");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AHostThatDeduplicatesNothingExportsNoExchangeCountersAtAll()
    {
        // duplicates_refused_total 0 states "checked, and clean" while meaning "nothing is
        // checking" -- and a flat counter is exactly what an alert rule reads as a healthy link.
        TelemetryStreamingServer server = Host();

        Read(server).Samples.Keys.Should().NotContain(key => key.StartsWith("telemetry_exchange_", StringComparison.Ordinal));

        server.Duplicates = new DuplicateFilter();
        Read(server).Samples.Should().ContainKey("telemetry_exchange_duplicates_refused_total");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ALimitThatHasEvaluatedNothingIsNeverReportedAsInsideItsBand()
    {
        // A limit on a misspelled channel is silent, and so is a limit on a healthy converter.
        // limit_breached 0 for the first tells an alerting system that an unprotected machine is
        // safe, which does not merely fail to warn -- it suppresses the warning.
        var monitor = new LimitMonitor([ChannelLimit.Parse("bus_voltage[V] in 370..420")]);
        TelemetryStreamingServer server = Host();
        server.Limits = monitor;

        MetricsExpositionParser.Document quiet = Read(server);
        quiet.Samples.Keys.Should().NotContain(key => key.StartsWith("telemetry_limit_breached", StringComparison.Ordinal));
        quiet.Samples["telemetry_limits_unarmed"].Should().Be("1");
        quiet.Samples["telemetry_limit_armed{limit=\"bus_voltage[V] in 370..420\",channel=\"bus_voltage\"}"]
            .Should().Be("0");

        monitor.Evaluate(Channel, 411.5, "V", DateTime.UtcNow);

        MetricsExpositionParser.Document armed = Read(server);
        armed.Samples[$"telemetry_limit_breached{{limit=\"bus_voltage[V] in 370..420\",channel=\"{Channel}\"}}"]
            .Should().Be("0", "this one was evaluated, so its zero is a measurement");
        armed.Samples["telemetry_limits_unarmed"].Should().Be("0");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ALimitDisarmedByAUnitItDoesNotUnderstandIsNotReportedAsWatching()
    {
        // The one failure mode of an alarm that has no symptom at all: it cannot fire, and it
        // looks exactly like an alarm that has nothing to say.
        var monitor = new LimitMonitor([ChannelLimit.Parse("bus_voltage[V] in 370..420")]);
        monitor.Evaluate(Channel, 411.5, "A", DateTime.UtcNow);

        TelemetryStreamingServer server = Host();
        server.Limits = monitor;

        MetricsExpositionParser.Document document = Read(server);

        document.Samples.Keys.Should().NotContain(key => key.StartsWith("telemetry_limit_breached", StringComparison.Ordinal));
        document.Samples[$"telemetry_limit_armed{{limit=\"bus_voltage[V] in 370..420\",channel=\"{Channel}\"}}"]
            .Should().Be("0");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ANodeNeverHeardFromHasNoLastContactAndIsStillCountedAsExpected()
    {
        // The two halves of §1 in one document. The zero on samples_total is measured -- the
        // ledger expected this node and counted nothing -- while the absent timestamp is the
        // thing that was never measured. A zero there dates its last contact to 1970.
        var ledger = new CoverageLedger();
        ledger.Expect("NODE-SILENT");
        ledger.Expect("NODE-LIVE");
        ledger.RecordSample("NODE-LIVE");

        TelemetryStreamingServer server = Host();
        server.Coverage = ledger.Snapshot;

        MetricsExpositionParser.Document document = Read(server);

        document.Samples["telemetry_fleet_node_samples_total{node=\"NODE-SILENT\"}"].Should().Be("0");
        document.Samples["telemetry_fleet_node_reporting{node=\"NODE-SILENT\"}"].Should().Be("0");
        document.Samples.Should().NotContainKey("telemetry_fleet_node_last_heard_timestamp_seconds{node=\"NODE-SILENT\"}");
        document.Samples.Should().ContainKey("telemetry_fleet_node_last_heard_timestamp_seconds{node=\"NODE-LIVE\"}");
    }
}
