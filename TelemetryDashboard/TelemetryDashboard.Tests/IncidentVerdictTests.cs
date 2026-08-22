using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Turning an incident window from a data dump into a triage list.
/// </summary>
/// <remarks>
/// <c>/api/incident</c> handed back every channel's window and said nothing about any of them, so
/// at three in the morning somebody had to read thirty series to find the one that moved.
/// <para>
/// Measured on a live host archiving a replay with a 6 V spike that recovers before the window
/// ends, a channel of pure 20 mV noise, and a channel with two samples: the triage list named
/// <c>RIG.spiking</c> alone, at 298.8 sigma on sample 181 of 300 — the spike starts at 180 in the
/// generator — while the noise channel's 3.58 sigma stayed inside its bar and the sparse channel
/// came back "not judged" rather than healthy.
/// </para>
/// </remarks>
public class IncidentVerdictTests
{
    private sealed class Store : IDataLogger
    {
        private readonly List<TelemetryPacket> _packets = new();

        public void Seed(TelemetryPacket packet) => _packets.Add(packet);
        public Task WriteAsync(TelemetryPacket p, CancellationToken c = default) { _packets.Add(p); return Task.CompletedTask; }
        public Task WriteBatchAsync(IEnumerable<TelemetryPacket> p, CancellationToken c = default) { _packets.AddRange(p); return Task.CompletedTask; }

        public Task<IEnumerable<TelemetryPacket>> QueryAsync(QueryFilter filter, CancellationToken c = default) =>
            Task.FromResult(_packets
                .Where(p => filter.StartTime is null || p.Timestamp >= filter.StartTime)
                .Where(p => filter.EndTime is null || p.Timestamp <= filter.EndTime)
                .Take(filter.Limit)
                .AsEnumerable());
    }

    private static readonly DateTime Instant = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Deterministic noise: no Random, so a failure here is always the same failure.</summary>
    private static double Wobble(int i) => 0.02 * Math.Sin(i * 2.399963) + 0.013 * Math.Sin(i * 5.700141);

    // ---- what a window verdict is -------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void AFaultThatHasRecoveredByTheEndOfTheWindowIsStillNamed()
    {
        // The reason a window needs its own verdict. An incident window runs from before a fault to
        // after it, so its newest sample is the recovery -- and Evaluate, which judges the newest
        // sample, reports "normal" for exactly the channel that caused the alarm.
        var samples = new List<double>();
        for (int i = 0; i < 300; i++) samples.Add(48.0 + Wobble(i) + (i is >= 180 and < 195 ? 6.0 : 0.0));

        var engine = new AnomalyEngine();

        engine.Evaluate(samples.ToArray()).IsAnomaly.Should().BeFalse("the last sample is the recovery");
        AnomalyEvaluation window = engine.EvaluateWindow(samples);

        window.IsAnomaly.Should().BeTrue();
        window.ZScore.Should().BeGreaterThan(50);
        window.Reason.Should().Contain("sample 181 of 300", "the excursion starts at index 180");
    }

    [Theory]
    [Trait("Category", "Tier1")]
    [InlineData(60)]
    [InlineData(300)]
    [InlineData(1500)]
    public void PureNoiseIsNotCalledAnomalousHoweverLongTheWindow(int count)
    {
        // The defect this found on a live host: a channel carrying nothing but 20 mV of noise came
        // back anomalous at 3.16 sigma over 128 samples, because the largest of many draws from
        // noise is large by construction -- near sqrt(2 ln n), which is 3.1 at n = 128. Against a
        // fixed 3 sigma bar, a thirty-channel rig would have had ten channels on its triage list
        // every time, and a list that is usually wrong stops being read.
        var samples = new List<double>();
        for (int i = 0; i < count; i++) samples.Add(48.0 + Wobble(i));

        AnomalyEvaluation verdict = new AnomalyEngine().EvaluateWindow(samples);

        verdict.IsAnomaly.Should().BeFalse(verdict.Reason);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheBarGrowsWithTheWindowAndNeverFallsBelowTheLiveDetectorsOwn()
    {
        AnomalyEngine.BarFor(128).Should().BeApproximately(Math.Sqrt(2 * Math.Log(128)) + 1.0, 1e-9);
        AnomalyEngine.BarFor(1000).Should().BeGreaterThan(AnomalyEngine.BarFor(128));
        AnomalyEngine.BarFor(2).Should().BeGreaterThanOrEqualTo(3.0, "never laxer than the live path");
        AnomalyEngine.BarFor(1).Should().Be(3.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TooFewSamplesIsARefusalToJudgeRatherThanACleanBillOfHealth()
    {
        // "I could not tell" and "nothing was wrong" are the same to anyone reading only the
        // boolean, and an operator who cannot separate them reads an unmonitored channel as healthy.
        AnomalyEvaluation verdict = new AnomalyEngine().EvaluateWindow(new[] { 1.0, 2.0 });

        verdict.IsAnomaly.Should().BeFalse();
        verdict.Reason.Should().StartWith("Not judged");
        verdict.ProcessedSampleCount.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AWindowThatNeverMovedSaysSoRatherThanReportingCalm()
    {
        AnomalyEvaluation verdict = new AnomalyEngine().EvaluateWindow(Enumerable.Repeat(48.0, 200).ToList());

        verdict.IsAnomaly.Should().BeFalse();
        verdict.Reason.Should().Contain("Nothing moved");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void NonFiniteReadingsAreDroppedRatherThanScoredAsZero()
    {
        var samples = new List<double> { 48, 48, double.NaN, 48, double.PositiveInfinity, 48, 48, 48 };

        new AnomalyEngine().EvaluateWindow(samples).ProcessedSampleCount.Should().Be(6);
    }

    // ---- as the endpoint reports it -------------------------------------------

    private static Store RigWithOneSpike()
    {
        var store = new Store();
        for (int i = 0; i < 300; i++)
        {
            DateTime at = Instant.AddSeconds(-300 + i);
            store.Seed(new TelemetryPacket("RIG", "spiking", 48.0 + Wobble(i) + (i is >= 180 and < 195 ? 6.0 : 0.0), "V", at));
            store.Seed(new TelemetryPacket("RIG", "quiet", 48.0 + Wobble(i + 7), "V", at));
        }
        store.Seed(new TelemetryPacket("RIG", "sparse", 12.3, "V", Instant.AddSeconds(-200)));
        store.Seed(new TelemetryPacket("RIG", "sparse", 12.3, "V", Instant.AddSeconds(-100)));
        return store;
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task TheTriageListNamesOnlyTheChannelThatMoved()
    {
        IncidentEndpoint.Result result =
            await IncidentEndpoint.QueryAsync(RigWithOneSpike(), Instant, leadSec: 400, trailSec: 2, node: null);

        result.Anomalous.Should().Equal(new[] { "RIG.spiking" });
        result.UnjudgedChannels.Should().Be(1, "the two-sample channel is unjudged, not healthy");
        result.ChannelCount.Should().Be(3);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task EveryChannelCarriesItsOwnVerdictAndTheReasonBehindIt()
    {
        IncidentEndpoint.Result result =
            await IncidentEndpoint.QueryAsync(RigWithOneSpike(), Instant, leadSec: 400, trailSec: 2, node: null);

        IncidentEndpoint.ChannelWindow spiking = result.Channels.Single(c => c.Variable == "spiking");
        IncidentEndpoint.ChannelWindow quiet = result.Channels.Single(c => c.Variable == "quiet");
        IncidentEndpoint.ChannelWindow sparse = result.Channels.Single(c => c.Variable == "sparse");

        spiking.IsAnomaly.Should().BeTrue();
        spiking.PeakZScore.Should().BeGreaterThan(50);
        spiking.Verdict.Should().Contain("Worst moment");

        quiet.IsAnomaly.Should().BeFalse();
        quiet.Verdict.Should().Contain("would be expected to reach anyway");

        sparse.Verdict.Should().StartWith("Not judged");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task TheChannelListKeepsItsStableOrderSoTwoIncidentsCanBeDiffed()
    {
        // The triage list is where the ranking lives. Reordering Channels by score would make a
        // client comparing two incidents read a reordering as a change.
        IncidentEndpoint.Result result =
            await IncidentEndpoint.QueryAsync(RigWithOneSpike(), Instant, leadSec: 400, trailSec: 2, node: null);

        result.Channels.Select(c => c.Variable).Should().Equal(new[] { "quiet", "sparse", "spiking" });
    }
}
