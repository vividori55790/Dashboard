using System.Globalization;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Cluster;
using TelemetryDashboard.Core.Query;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The document <c>/metrics</c> produces is one a scraper will accept and read as intended.
/// </summary>
/// <remarks>
/// Syntax rather than meaning, because the two fail differently and only one of them fails loudly.
/// A malformed line is rejected by the scraper and somebody finds out; a well-formed line carrying
/// the wrong escape, the wrong unit or a counter suffix on something that is not monotonic is
/// accepted, stored and queried for months.
/// <para>
/// Read back with <see cref="MetricsExpositionParser"/>, whose grammar comes from the specification
/// rather than from the writer, so these assertions are not the endpoint agreeing with itself.
/// </para>
/// </remarks>
public class MetricsExpositionFormatTests
{
    /// <summary>A host with every optional collaborator attached, so no family is skipped.</summary>
    private static TelemetryStreamingServer FullyFurnished()
    {
        var clocks = new TimeSyncJitterBuffer();
        clocks.SyncNodeClock("PEER-01", 100.4, 100.0);
        clocks.SyncNodeClock("PEER-01", 200.9, 200.0);

        var ledger = new CoverageLedger();
        ledger.Expect("NODE-LIVE");
        ledger.RecordSample("NODE-LIVE");

        var limits = new LimitMonitor([ChannelLimit.Parse("bus_voltage[V] in 370..420")]);
        limits.Evaluate("SIM:COM3.dab.bus_voltage", 411.5, "V", DateTime.UtcNow);

        var server = new TelemetryStreamingServer(port: 0)
        {
            Clocks = clocks.ObservedClocks,
            Coverage = ledger.Snapshot,
            Duplicates = new DuplicateFilter(),
            Limits = limits
        };

        double now = SeriesClock.UtcNowSec();
        for (int i = 9; i >= 0; i--) server.Series.Append("SIM:COM3.dab.bus_voltage", 411.5 + i, now - i);

        return server;
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheWholeDocumentParsesAsPrometheusTextExposition()
    {
        string text = MetricsEndpoint.Render(FullyFurnished());

        MetricsExpositionParser.Document document = MetricsExpositionParser.Parse(text);

        document.Samples.Should().NotBeEmpty("a furnished host that exports nothing would pass "
            + "every absence test in this suite for the wrong reason");
        text.Should().EndWith("\n", "the last line must end with a line feed");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AHostWithNothingAttachedStillProducesADocumentAScraperAccepts()
    {
        // The state a bench host is actually in: no ledger, no clocks, no limits, no samples. It
        // must not be an empty body, a parse error or a page of zeros.
        MetricsExpositionParser.Document document =
            MetricsExpositionParser.Parse(MetricsEndpoint.Render(new TelemetryStreamingServer(port: 0)));

        document.Samples.Should().ContainKey("telemetry_samples_accepted_total");
        document.Samples.Keys.Should().NotContain(key =>
            key.StartsWith("telemetry_node_clock", StringComparison.Ordinal)
            || key.StartsWith("telemetry_fleet_", StringComparison.Ordinal)
            || key.StartsWith("telemetry_limit", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void EveryFamilyThatDeclaresAHeaderAlsoCarriesASample()
    {
        // The structural form of this endpoint's rule. A header with no series beneath it is the
        // shape a zero-emitting implementation takes on the way to being written, so the writer
        // holds the header back until a sample proves the family has something to say.
        MetricsExpositionParser.Document document =
            MetricsExpositionParser.Parse(MetricsEndpoint.Render(FullyFurnished()));

        string[] families = document.Samples.Keys.Select(MetricsExpositionParser.Family).Distinct().ToArray();

        document.Types.Keys.Should().BeEquivalentTo(families);
        document.Help.Keys.Should().BeEquivalentTo(families);

        // The case a fully furnished host cannot show, and the one a live --simulate run is
        // actually in: a collaborator attached and holding nothing. Its families must vanish
        // entirely rather than leave three headers standing over no series.
        var attachedAndEmpty = new TelemetryStreamingServer(port: 0)
        {
            Clocks = new TimeSyncJitterBuffer().ObservedClocks
        };

        MetricsExpositionParser.Document quiet =
            MetricsExpositionParser.Parse(MetricsEndpoint.Render(attachedAndEmpty));

        quiet.Types.Keys.Should().BeEquivalentTo(
            quiet.Samples.Keys.Select(MetricsExpositionParser.Family).Distinct());
        quiet.Types.Keys.Should().NotContain(family => family.StartsWith("telemetry_node_clock", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void LabelValuesEscapeTheThreeSequencesTheFormatDefinesAndNoOthers()
    {
        // Channel names arrive from the wire and are not tame. A raw quote ends the label early
        // and the rest of the name becomes syntax; a raw line feed ends the sample line itself.
        var server = new TelemetryStreamingServer(port: 0);
        server.Series.Append("odd\"name\\with\nbreak", 1.0, SeriesClock.UtcNowSec());

        string text = MetricsEndpoint.Render(server);

        text.Should().Contain("channel=\"odd\\\"name\\\\with\\nbreak\"");
        MetricsExpositionParser.Parse(text).Samples.Should()
            .ContainKey("telemetry_channel_value{channel=\"odd\\\"name\\\\with\\nbreak\"}");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void NonFiniteReadingsUseTheFormatsOwnSpellingsRatherThanDotNets()
    {
        // .NET spells these "Infinity" and "-Infinity", which a scraper rejects outright. A sensor
        // that reported NaN was read, so it is exported rather than withheld -- absent means
        // nobody measured, and these are not that.
        var server = new TelemetryStreamingServer(port: 0);
        double now = SeriesClock.UtcNowSec();
        server.Series.Append("nan", double.NaN, now);
        server.Series.Append("high", double.PositiveInfinity, now);
        server.Series.Append("low", double.NegativeInfinity, now);

        IReadOnlyDictionary<string, string> samples =
            MetricsExpositionParser.Parse(MetricsEndpoint.Render(server)).Samples;

        samples["telemetry_channel_value{channel=\"nan\"}"].Should().Be("NaN");
        samples["telemetry_channel_value{channel=\"high\"}"].Should().Be("+Inf");
        samples["telemetry_channel_value{channel=\"low\"}"].Should().Be("-Inf");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ADecimalPointStaysAPointUnderACommaCulture()
    {
        // This host ships to plant floors in locales whose default number format writes 411,5.
        // A scraper reads that as a label separator or as an unreadable line, so the failure is a
        // machine that exports nothing readable -- discovered by the operator, in their locale.
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var server = new TelemetryStreamingServer(port: 0);
            server.Series.Append("bus", 411.5, SeriesClock.UtcNowSec());

            MetricsExpositionParser.Parse(MetricsEndpoint.Render(server))
                .Samples["telemetry_channel_value{channel=\"bus\"}"].Should().Be("411.5");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void OnlyCountersCarryTheTotalSuffixAndNoNameCarriesANonBaseUnit()
    {
        // Both halves are naming conventions rather than syntax, so nothing rejects a breach of
        // them: _total on a gauge makes rate() report a rate that does not exist, and a name in
        // milliseconds silently disagrees with every other exporter on the same dashboard.
        MetricsExpositionParser.Document document =
            MetricsExpositionParser.Parse(MetricsEndpoint.Render(FullyFurnished()));

        foreach ((string family, string type) in document.Types)
        {
            family.EndsWith("_total", StringComparison.Ordinal).Should().Be(type == "counter",
                $"{family} is a {type}; the _total suffix is a convention for counters and only for them");

            family.Should().StartWith(MetricsEndpoint.Prefix, "every name carries the application prefix");
        }

        string[] nonBaseUnits = ["_ms", "_msec", "_millis", "_kb", "_mb", "_bits", "_percent", "_minutes", "_hours"];
        document.Types.Keys.Should().NotContain(
            family => nonBaseUnits.Any(unit => family.EndsWith(unit, StringComparison.Ordinal)),
            "the naming conventions require base units: seconds, bytes, ratios");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheScrapePathIsAdvertisedSoTheBannerAndAScraperAgreeAboutIt()
    {
        // The banner prints this list. An endpoint that answers and is not on it is one an
        // operator has to already know about in order to find.
        TelemetryStreamingServer.AdvertisedEndpoints.Should().Contain(MetricsEndpoint.Path);
        MetricsEndpoint.Path.Should().Be("/metrics", "every scraper defaults to this exact path");
        MetricsEndpoint.ContentType.Should().Contain("version=0.0.4",
            "a missing version lets the scraper's build decide what this document means");
    }
}
