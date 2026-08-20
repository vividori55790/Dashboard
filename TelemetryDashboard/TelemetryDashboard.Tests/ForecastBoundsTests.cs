using System;
using System.Linq;
using FluentAssertions;
using TelemetryDashboard.Core.Analytics;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Covers when the engine is allowed to state a forecast, and when it must not.
/// </summary>
/// <remarks>
/// Every case here comes from something a live feed did. Wikimedia's page-size channel is noise
/// around a few thousand bytes, and the engine confidently forecast it at minus 228,000 sixty
/// seconds ahead: a least-squares line always exists, and continuing one fitted to scatter produces
/// arithmetic rather than a prediction.
///
/// Two guards resulted, and they answer different questions. Is there a trend at all? And does
/// continuing it land anywhere the channel's own history reaches? A forecast has to pass both.
/// </remarks>
public class ForecastBoundsTests
{
    private const string Channel = "NODE.CHANNEL";

    /// <summary>Feeds every value in order and returns the last verdict.</summary>
    /// <remarks>
    /// A plain foreach, deliberately. Writing this as <c>values.Select(...).Last()</c> looks
    /// equivalent and is not: LINQ optimises Last() over an array-backed Select to evaluate only
    /// the final element, so the engine was called once instead of sixty times and every channel
    /// stayed in warm-up. A test that quietly does almost nothing still passes whichever
    /// assertions happen not to depend on the work.
    /// </remarks>
    private static AnomalyResult Feed(TelemetryMlAnalyticsEngine engine, params double[] values)
    {
        AnomalyResult? last = null;
        foreach (double value in values) last = engine.AnalyzeChannel(Channel, value);
        return last ?? throw new ArgumentException("Feed needs at least one value.", nameof(values));
    }

    private static double[] Noise(int count, double centre, double spread, int seed)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, count)
            .Select(_ => centre + (random.NextDouble() - 0.5) * spread)
            .ToArray();
    }

    [Fact]
    public void ANoisyChannelGetsNoForecastAtAll()
    {
        var engine = new TelemetryMlAnalyticsEngine();

        AnomalyResult result = Feed(engine, Noise(60, centre: 3_000, spread: 4_000, seed: 11));

        result.HasVerdict.Should().BeTrue("there is plenty of history to score against");
        result.HasForecast.Should().BeFalse("a line through scatter is not a trend");
        result.TrendRSquared.Should().BeLessThan(TrendFit.MinimumRSquared);
    }

    [Fact]
    public void ACleanRampIsForecastWhenTheHorizonIsComparableToTheWindow()
    {
        // 0.5 Hz over a 50-sample window is 100 seconds of history, so predicting 60 seconds ahead
        // is an extrapolation of well under one window. That is the regime a forecast belongs in.
        var engine = new TelemetryMlAnalyticsEngine(windowSize: 50, sampleRateHz: 0.5);

        AnomalyResult result = Feed(engine, Enumerable.Range(0, 60).Select(i => 100.0 + i * 0.5).ToArray());

        result.HasForecast.Should().BeTrue("this is exactly the case a forecast exists for");
        result.ForecastLeavesObservedRange.Should().BeFalse();
        result.ForecastHorizonSec.Should().Be(60, "the full horizon is inside this channel's reach");
        result.PredictedValueIn60s.Should().BeGreaterThan(result.Mean);
    }

    [Fact]
    public void ADefaultRateChannelIsForecastOnlyAsFarAsItsHistoryReaches()
    {
        // Recorded as a finding, not a preference. The default window holds 50 samples at 20 Hz —
        // two and a half seconds — and the published field is called Predicted60s. Continuing a
        // slope twenty-four times further than it was observed is not a prediction, and a clean
        // ramp is refused for the same reason noise is. The number was always unsupportable; only
        // the noisy channels made it obvious, by coming out negative.
        //
        // The fix is not a looser bound. It is either a window long enough to justify the horizon,
        // or a horizon scaled to the window. Until one of those is chosen, this is what the engine
        // honestly reports.
        var engine = new TelemetryMlAnalyticsEngine(windowSize: 50, sampleRateHz: 20.0);

        AnomalyResult result = Feed(engine, Enumerable.Range(0, 60).Select(i => 100.0 + i * 0.5).ToArray());

        result.TrendRSquared.Should().BeGreaterThan(0.99, "the ramp is perfectly linear");

        // The engine no longer refuses outright. It looks as far ahead as the channel's history
        // reaches -- a few seconds here rather than sixty -- and says how far that was. Silence
        // would have been honest and useless; this is honest and usable.
        result.HasForecast.Should().BeTrue();
        result.ForecastHorizonSec.Should().BeGreaterThan(0).And.BeLessThan(60,
            "2.5 seconds of history does not reach a minute ahead, and the number says so");
        result.ForecastLeavesObservedRange.Should().BeFalse();
    }

    [Fact]
    public void ATrendHeadingSomewhereTheChannelHasNeverBeenIsReportedAsSuch_NotAsANumber()
    {
        var engine = new TelemetryMlAnalyticsEngine();

        // A steep, well-fitted fall. Continuing it for sixty seconds lands far below anything this
        // channel has ever read — which is the shape that produced a negative page size.
        AnomalyResult result = Feed(engine, Enumerable.Range(0, 60).Select(i => 10_000.0 - i * 150.0).ToArray());

        result.TrendRSquared.Should().BeGreaterThan(TrendFit.MinimumRSquared, "the trend itself is real");

        // The answer is a nearer horizon, not a clamped number and not silence. Clamping would
        // invent a value; silence would be indistinguishable from having no trend at all, which is
        // a different and far less urgent situation.
        result.HasForecast.Should().BeTrue();
        result.ForecastHorizonSec.Should().BeLessThan(60, "the fall leaves the observed range sooner");
        // Stated as the guarantee actually is, not as one that sounds stronger. The bound is
        // symmetric and derived from the window alone: within one width of the observed range,
        // either side. It knows nothing about signs, and it must not -- a freezer falling through
        // 0 °C is precisely the crossing an operator wants predicted, and a rule that refused to
        // forecast below zero would suppress it. What the bound guarantees is that the projection
        // stays in the neighbourhood the channel has actually occupied.
        (double min, double max) = (1_150.0, 8_500.0);   // the last 50 samples of this ramp
        double width = max - min;
        result.PredictedValueIn60s.Should().BeInRange(min - width, max + width);
    }

    [Fact]
    public void TheTwoWaysOfHavingNoForecastAreDistinguishable()
    {
        var noisy = new TelemetryMlAnalyticsEngine();
        AnomalyResult scatter = Feed(noisy, Noise(60, centre: 500, spread: 900, seed: 3));

        var steep = new TelemetryMlAnalyticsEngine();
        AnomalyResult runaway = Feed(steep, Enumerable.Range(0, 60).Select(i => 500.0 - i * 40.0).ToArray());

        // A wandering channel gets no forecast: there is no direction to carry forward.
        scatter.HasForecast.Should().BeFalse();
        scatter.ForecastHorizonSec.Should().Be(0);

        // A channel with a real direction gets one, and the horizon is the honest part. A short
        // horizon on a steep trend is the system saying "this is going somewhere, and I can only
        // see this far" -- which is more useful than either a confident distant number or silence.
        runaway.HasForecast.Should().BeTrue();
        runaway.ForecastHorizonSec.Should().BeGreaterThan(0).And.BeLessThan(60);
    }

    [Fact]
    public void AWarmingUpChannelClaimsNeitherAVerdictNorAForecast()
    {
        var engine = new TelemetryMlAnalyticsEngine();

        AnomalyResult result = engine.AnalyzeChannel(Channel, 42.0);

        result.HasVerdict.Should().BeFalse();
        result.HasForecast.Should().BeFalse();
        result.ForecastLeavesObservedRange.Should().BeFalse(
            "nothing is known yet, so nothing is claimed — including that a trend is running away");
    }

    [Fact]
    public void AFlatChannelIsNeverForecastToMove()
    {
        var engine = new TelemetryMlAnalyticsEngine();

        AnomalyResult result = Feed(engine, Enumerable.Repeat(20.0, 60).ToArray());

        // A constant offers no basis for predicting a change, and its observed range has zero width
        // so nothing but its own value could pass the bound anyway.
        result.HasForecast.Should().BeFalse();
        result.PredictedValueIn60s.Should().Be(20.0);
    }

    [Fact]
    public void TheAllowanceScalesWithTheChannelRatherThanAFixedNumber()
    {
        // A volatile channel earns a wide allowance and a steady one a narrow allowance. That is
        // the right way round: predicting a large move for a quantity that has never moved is
        // precisely the claim that needs evidence.
        // Both at 0.5 Hz so the horizon is inside the window, isolating the range test itself.
        var volatileEngine = new TelemetryMlAnalyticsEngine(windowSize: 50, sampleRateHz: 0.5);
        AnomalyResult wide = Feed(volatileEngine,
            Enumerable.Range(0, 60).Select(i => 1_000.0 + i * 20.0).ToArray());

        var steadyEngine = new TelemetryMlAnalyticsEngine(windowSize: 50, sampleRateHz: 0.5);
        AnomalyResult narrow = Feed(steadyEngine,
            Enumerable.Range(0, 60).Select(i => 1_000.0 + i * 0.02).ToArray());

        wide.HasForecast.Should().BeTrue("its own history covers a wide range");
        narrow.HasForecast.Should().BeTrue("a small move on a steady channel is still within reach");
    }
}
