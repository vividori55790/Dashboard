using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Records;
using TelemetryDashboard.Host.Ingest;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Seeing a fault that never trips a threshold.
/// </summary>
/// <remarks>
/// A rolling z-score measures a reading against the window it just came from, so a channel moving
/// slowly enough drags its own baseline along and never scores. Everything stays inside its limits,
/// every z-score stays near zero, and the unit has been getting worse for weeks.
/// <para>
/// Measured on a live host replaying a 48 V rail sagging 0.4 V over two minutes under noise sixty
/// times larger than its per-sample slope, beside an identical healthy channel:
/// <c>sagging.drift</c> came out negative on 339 of 341 samples (mean -0.0263) and
/// <c>healthy.drift</c> on 177 of 341 (mean -0.0009, a coin flip). The raw channels were
/// indistinguishable — peak |z| of 3.33 for the sagging one against 3.41 for the healthy one, so
/// the detector this product already had scored the healthy channel as the more anomalous of the
/// two.
/// </para>
/// </remarks>
public class DriftDetectionTests
{
    // ---- the average underneath -----------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheFirstSampleSeedsTheAverageRatherThanRampingUpToIt()
    {
        // Starting from zero injects a climb from zero to the channel's operating point that looks
        // exactly like a real transient -- and to a drift monitor, exactly like the drift it is for.
        var average = new ExponentialAverage();

        average.HasValue.Should().BeFalse();
        average.Value.Should().Be(double.NaN);

        average.Update(48.0, 0.01).Should().Be(48.0, "a 1 % alpha must not leave it at 0.48");
    }

    [Theory]
    [Trait("Category", "Tier2")]
    [InlineData(2.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void AnOutOfRangeAlphaIsClampedRatherThanRefused(double alpha)
    {
        // A caller deriving alpha from a measured time gap can land marginally outside [0,1] through
        // rounding alone, and refusing would make the average stop tracking on a rounding error.
        var average = new ExponentialAverage();
        average.Update(10.0, 0.5);

        average.Update(20.0, alpha).Should().BeInRange(10.0, 20.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AMemoryInSecondsMeansTheSameThingAtAnySampleRate()
    {
        // The reason alpha is computed per update rather than stored. A fixed per-sample constant
        // gives a rig sampling at 20 Hz a memory of seconds and one sampling at 1 Hz a memory of
        // minutes, from the same setting.
        double oneTimeConstant = ExponentialAverage.AlphaForTimeConstant(elapsedSeconds: 5, timeConstantSeconds: 5);
        oneTimeConstant.Should().BeApproximately(1 - Math.Exp(-1), 1e-12);

        ExponentialAverage.AlphaForTimeConstant(0, 5).Should().Be(0.0, "no time passed, so nothing is folded in");
        ExponentialAverage.AlphaForTimeConstant(5, 0).Should().Be(1.0, "no memory at all is the raw signal");
    }

    // ---- what drift is, and is not --------------------------------------------

    private static double? Feed(DriftMonitor monitor, Func<int, double> signal, int samples, double stepSeconds)
    {
        double? drift = null;
        for (int i = 0; i < samples; i++) drift = monitor.Update(signal(i), stepSeconds);
        return drift;
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ASteadyChannelDoesNotDrift()
    {
        var monitor = new DriftMonitor { FastSeconds = 1, SlowSeconds = 30, WarmUpSeconds = 30 };

        double? drift = Feed(monitor, _ => 48.0, samples: 600, stepSeconds: 0.1);

        drift.Should().NotBeNull("60 seconds is past the warm-up");
        drift!.Value.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ARampDriftsInItsOwnDirectionAndKeepsDrifting()
    {
        // The signal a z-score cannot see: each step is far below the channel's own noise, and the
        // rolling baseline follows it down.
        var monitor = new DriftMonitor { FastSeconds = 1, SlowSeconds = 30, WarmUpSeconds = 30 };

        double? drift = Feed(monitor, i => 48.0 - 0.001 * i, samples: 900, stepSeconds: 0.1);

        drift.Should().NotBeNull();
        drift!.Value.Should().BeLessThan(-0.1, "a falling channel drifts negative, and visibly");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AStepShowsBrieflyAndThenDecaysBecauseAStepIsNotDrift()
    {
        // Deliberate, and the property most easily mistaken for a bug. A step is a transient and
        // the z-score already catches it; drift is for the movement nothing else can see.
        var monitor = new DriftMonitor { FastSeconds = 1, SlowSeconds = 20, WarmUpSeconds = 20 };
        Feed(monitor, _ => 48.0, samples: 400, stepSeconds: 0.1);

        double? justAfter = Feed(monitor, _ => 49.0, samples: 30, stepSeconds: 0.1);
        double? longAfter = Feed(monitor, _ => 49.0, samples: 2000, stepSeconds: 0.1);

        justAfter!.Value.Should().BeGreaterThan(0.4, "the fast average moves first");
        longAfter!.Value.Should().BeApproximately(0.0, 0.01, "the slow average caught up; the step is over");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheSameRampAtTwoSampleRatesGivesTheSameDrift()
    {
        // The whole reason the memory is stated in seconds. Two rigs watching the same physical
        // ramp at different rates must report the same drift, or the number means nothing without
        // also knowing the link's rate.
        var fast = new DriftMonitor { FastSeconds = 2, SlowSeconds = 60, WarmUpSeconds = 60 };
        var slow = new DriftMonitor { FastSeconds = 2, SlowSeconds = 60, WarmUpSeconds = 60 };

        // 0.02 V per second, for 200 seconds, sampled at 10 Hz and at 1 Hz.
        double? atTenHertz = Feed(fast, i => 48.0 - 0.002 * i, samples: 2000, stepSeconds: 0.1);
        double? atOneHertz = Feed(slow, i => 48.0 - 0.02 * i, samples: 200, stepSeconds: 1.0);

        atTenHertz.Should().NotBeNull();
        atOneHertz.Should().NotBeNull();
        atOneHertz!.Value.Should().BeApproximately(atTenHertz!.Value, 0.05);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void NothingIsReportedUntilTheBaselineHasHadTimeToMeanSomething()
    {
        // Zero would be a measurement -- "this channel is not drifting" -- offered during the very
        // window where the answer is unknown.
        var monitor = new DriftMonitor { FastSeconds = 1, SlowSeconds = 60, WarmUpSeconds = 60 };

        Feed(monitor, _ => 48.0, samples: 100, stepSeconds: 0.1).Should().BeNull("only 10 seconds in");
        monitor.IsWarm.Should().BeFalse();

        Feed(monitor, _ => 48.0, samples: 600, stepSeconds: 0.1).Should().NotBeNull();
        monitor.IsWarm.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ResettingForgetsTheBaselineSoARigChangeIsNotReadAsDrift()
    {
        var monitor = new DriftMonitor { FastSeconds = 1, SlowSeconds = 20, WarmUpSeconds = 20 };
        Feed(monitor, _ => 48.0, samples: 400, stepSeconds: 0.1);

        monitor.Reset();

        monitor.IsWarm.Should().BeFalse();
        monitor.Update(12.0, 0.1).Should().BeNull("12 V is a different machine, not a 36 V excursion");
    }

    // ---- as the host publishes it ---------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheShortMemoryFollowsTheLongOneSoTheTwoCannotCollapseTogether()
    {
        // The defect this replaced. The window set only the long memory and left the short one at a
        // fixed thirty seconds, so any window near or below that gave two averages tracking each
        // other and a difference that was noise. Measured on a live host: at a 40-second window,
        // nothing was ever published at all.
        foreach (int window in new[] { 10, 40, 900, 7200 })
        {
            var projection = new ChannelDriftProjection(window);

            projection.SlowSeconds.Should().Be(window);
            (projection.SlowSeconds / projection.FastSeconds).Should()
                .BeApproximately(ChannelDriftProjection.MemoryRatio, 1e-9,
                    $"a {window}s window must keep the two memories a long way apart");
        }
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task WithoutTheFlagNoDriftChannelAppears()
    {
        var published = new List<string>();
        var path = new IngestRecordPath(
            (packet, _, _) => { published.Add(packet.Variable); return ValueTask.CompletedTask; },
            isSimulated: false);

        await path.OfferPacketAsync(new TelemetryPacket("MCU_A", "rail", 48.0, "V"), "COM3");

        published.Should().Equal(new[] { "rail" });
        path.Drift.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task ADriftChannelCarriesTheUnitOfTheChannelItMeasures()
    {
        // Drift is a difference between two averages of the same quantity, so it is volts on a
        // voltage. DerivedNumericProjection took one fixed unit for all its output until this, and
        // publishing drift unitless leaves an operator writing a limit against a number whose scale
        // they have to guess.
        var units = new List<(string Variable, string Unit)>();
        var path = new IngestRecordPath(
            (packet, _, _) => { units.Add((packet.Variable, packet.Unit)); return ValueTask.CompletedTask; },
            isSimulated: false,
            driftWindowSeconds: 1);

        DateTime start = DateTime.UtcNow.AddSeconds(-30);
        for (int i = 0; i < 40; i++)
        {
            await path.OfferPacketAsync(
                new TelemetryPacket("MCU_A", "rail", 48.0 - 0.01 * i, "V") { Timestamp = start.AddSeconds(i) },
                "COM3");
        }

        units.Should().Contain(u => u.Variable == "rail.drift", "a one-second window warms up quickly");
        units.Where(u => u.Variable == "rail.drift").Should().OnlyContain(u => u.Unit == "V");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task DriftIsNotMeasuredOnAnotherProjectionsOutput()
    {
        // With --watch-intervals also on, drift over an interval channel is arithmetic about the
        // link's timing rather than about the plant, and it would double an already doubled record
        // count for a figure nobody asked for.
        var published = new List<string>();
        var path = new IngestRecordPath(
            (packet, _, _) => { published.Add(packet.Variable); return ValueTask.CompletedTask; },
            isSimulated: false,
            watchIntervals: true,
            driftWindowSeconds: 1);

        DateTime start = DateTime.UtcNow.AddSeconds(-30);
        for (int i = 0; i < 40; i++)
        {
            await path.OfferPacketAsync(
                new TelemetryPacket("MCU_A", "rail", 48.0 - 0.01 * i, "V") { Timestamp = start.AddSeconds(i) },
                "COM3");
        }

        published.Should().Contain("rail.interval").And.Contain("rail.drift");
        published.Should().NotContain(v => v.Contains("interval.drift", StringComparison.Ordinal));
    }
}
