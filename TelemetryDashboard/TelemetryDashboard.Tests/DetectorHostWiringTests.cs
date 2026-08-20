using TelemetryDashboard.Core.Analytics.Detectors;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Streaming;
using TelemetryDashboard.Host.Ingest;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The detectors run on the path a real sample takes, not only in a unit test.
/// </summary>
/// <remarks>
/// This is the check the project's own reachability audit exists to force. A detector suite with
/// perfect coverage and no caller is the recurring defect in this codebase — a script engine, a
/// retry policy and a marketplace client each shipped that way — so the question here is not "does
/// the panel work" but "does a sample published by the host reach it".
/// </remarks>
public class DetectorHostWiringTests
{
    private static TelemetryPacket Sample(double value, DateTime at) => new()
    {
        NodeId = "NODE_1",
        Variable = "TEMP",
        Value = value,
        Unit = "C",
        Timestamp = at
    };

    [Fact]
    public async Task EverySamplePublishedByTheHostReachesEveryConfiguredDetector()
    {
        await using var server = new TelemetryStreamingServer(18098);
        var panel = new DetectorPanel(new IChannelDetector[]
        {
            new MedianAbsoluteDeviationDetector(window: 20, threshold: 3.5, label: "robust"),
            new RateOfChangeDetector(maxRatePerSecond: 25.0, label: "physical")
        });

        var publisher = new IngestPublisher(
            server, "REAL_HARDWARE", isSimulated: false, recorder: null,
            guard: new IngestRateGuard(), detectors: panel);

        publisher.Detectors.Should().BeSameAs(panel);

        DateTime at = DetectorSignals.Origin;
        var series = new List<double>(DetectorSignals.Wobble(20, 10.0, 0.1)) { 100.0 };

        foreach (double value in series)
        {
            await publisher.PublishAsync(Sample(value, at), "COM-TEST", CancellationToken.None);
            at += TimeSpan.FromMilliseconds(100);
        }

        publisher.SamplesPublished.Should().Be(series.Count);
        panel.Tallies.Should().AllSatisfy(t => t.Offered.Should().Be(series.Count,
            "the panel is asked on the publish path, once per sample"));

        panel.AnomaliesFlagged.Should().BeGreaterThan(0);
        panel.RecentFlags.Should().NotBeEmpty();
        panel.RecentFlags.Should().AllSatisfy(f => f.Channel.Should().Be("NODE_1.TEMP",
            "the detectors see the same fully qualified channel the wire and the recorder do"));
    }

    [Fact]
    public async Task ASimulatedSampleReachesTheDetectorsUnderItsMarkedName()
    {
        await using var server = new TelemetryStreamingServer(18097);
        var panel = new DetectorPanel(new IChannelDetector[]
        {
            new MedianAbsoluteDeviationDetector(window: 20, threshold: 3.5)
        });

        var publisher = new IngestPublisher(
            server, "SIMULATED", isSimulated: true, recorder: null,
            guard: new IngestRateGuard(), detectors: panel);

        await publisher.PublishAsync(Sample(1.0, DetectorSignals.Origin), "SIM", CancellationToken.None);

        panel.Detectors[0].CanHandle("SIM:NODE_1.TEMP").Should().BeTrue();
        panel.Tallies[0].Offered.Should().Be(1);
        panel.Tallies[0].Withheld.Should().Be(1, "one sample is not a baseline");
    }

    [Fact]
    public async Task AChannelTheRateGuardIsolatedNeverReachesTheDetectors()
    {
        await using var server = new TelemetryStreamingServer(18096);
        var panel = new DetectorPanel(new IChannelDetector[]
        {
            new MedianAbsoluteDeviationDetector(window: 20)
        });

        // A limit of one sample per second, then a burst: the guard isolates the channel and the
        // detectors must not see what was dropped, or their baseline would describe a stream the
        // host never served.
        var publisher = new IngestPublisher(
            server, "REAL_HARDWARE", isSimulated: false, recorder: null,
            guard: new IngestRateGuard(maxChannelRatePerSecond: 1), detectors: panel);

        for (int i = 0; i < 200; i++)
        {
            await publisher.PublishAsync(Sample(i, DetectorSignals.Origin), "COM-TEST", CancellationToken.None);
        }

        panel.Tallies[0].Offered.Should().Be(publisher.SamplesPublished,
            "the guard runs first, so the detectors see exactly what reached the wire");
        panel.Tallies[0].Offered.Should().BeLessThan(200, "the burst was above the configured limit");
    }

    [Fact]
    public async Task AHostWithNoConfigurationFileStillHasAPanel_ItSimplyJudgesWithNothingExtra()
    {
        await using var server = new TelemetryStreamingServer(18095);

        var publisher = new IngestPublisher(
            server, "REAL_HARDWARE", isSimulated: false, recorder: null, guard: new IngestRateGuard());

        publisher.Detectors.Should().NotBeNull(
            "the default path resolves the shared analytics configuration rather than leaving a null");

        await publisher.PublishAsync(Sample(1.0, DetectorSignals.Origin), "COM-TEST", CancellationToken.None);
        publisher.SamplesPublished.Should().Be(1);
    }
}
