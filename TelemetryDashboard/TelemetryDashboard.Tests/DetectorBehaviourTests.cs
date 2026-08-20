using TelemetryDashboard.Core.Analytics.Detectors;

namespace TelemetryDashboard.Tests;

/// <summary>
/// What each detector catches, and — the half that is usually left unstated — what it does not.
/// </summary>
/// <remarks>
/// Every test here runs a signal whose right answer is known by construction: a clean ramp, a single
/// outlier, a sustained step, a perfectly flat line. Several assert a <em>miss</em>, deliberately. A
/// detector whose limits are undocumented gets trusted outside them, and the way a limit stops being
/// folklore is a test that fails if the limit ever quietly changes.
/// </remarks>
public class DetectorBehaviourTests
{
    private const string Channel = "NODE_1.TEMP";

    // ---------------------------------------------------------------
    // Robust median / MAD
    // ---------------------------------------------------------------

    [Fact]
    public void Mad_KeepsCatchingOutliersAfterAZScoreBaselineHasBeenPoisonedByThem()
    {
        // Twenty calm samples, then five spikes spread out. Each spike enters the z-score's own
        // rolling baseline and inflates the sigma the next one is measured against; the median the
        // robust detector uses does not move at all.
        var series = new List<double>(DetectorSignals.Wobble(20, 10.0, 0.1));
        for (int i = 0; i < 5; i++)
        {
            series.AddRange(DetectorSignals.Wobble(3, 10.0, 0.1));
            series.Add(100.0);
        }

        var mad = new MedianAbsoluteDeviationDetector(window: 20, threshold: 3.5);
        var zscore = new RollingZScoreDetector(window: 20, threshold: 2.5);

        IReadOnlyList<DetectorVerdict> madVerdicts = DetectorSignals.Run(mad, Channel, series);
        IReadOnlyList<DetectorVerdict> zVerdicts = DetectorSignals.Run(zscore, Channel, series);

        madVerdicts.Flagged().Should().Be(5, "the median is unmoved by five spikes in a twenty-sample window");
        zVerdicts.Flagged().Should().Be(3, "it catches the first three; by the fourth its own baseline holds three spikes");

        int last = series.Count - 1;
        madVerdicts[last].IsAnomaly.Should().BeTrue();
        zVerdicts[last].IsAnomaly.Should().BeFalse(
            "by the last spike the z-score's baseline contains four others, so a fifth looks ordinary");
    }

    [Fact]
    public void Mad_DeclinesOnAPerfectlyFlatChannel_AndThereforeMissesAStepOutOfOne()
    {
        var series = new List<double>(DetectorSignals.Constant(20, 5.0)) { 9.0 };

        IReadOnlyList<DetectorVerdict> verdicts =
            DetectorSignals.Run(new MedianAbsoluteDeviationDetector(window: 20, threshold: 3.5), Channel, series);

        verdicts.Unjudged().Should().Be(series.Count, "a constant baseline offers no scale to divide by");
        verdicts[^1].HasVerdict.Should().BeFalse();
        verdicts[^1].Reason.Should().Contain("perfectly constant");

        // Stated as a miss rather than hidden as an edge case: this is the failure
        // RateOfChangeDetector exists to cover, and the panel is how both run at once.
        verdicts[^1].IsAnomaly.Should().BeFalse("no verdict was reached, so nothing was flagged");
    }

    // ---------------------------------------------------------------
    // EWMA level shift
    // ---------------------------------------------------------------

    [Fact]
    public void Ewma_CatchesASustainedShiftLongAfterMadHasAbsorbedItAsTheNewNormal()
    {
        var series = new List<double>(DetectorSignals.Wobble(40, 50.0, 0.5));
        series.AddRange(DetectorSignals.Wobble(25, 53.0, 0.5));

        var ewma = new EwmaLevelShiftDetector(trainingSamples: 40, lambda: 0.2, limitSigma: 3.0);
        var mad = new MedianAbsoluteDeviationDetector(window: 20, threshold: 3.5);

        IReadOnlyList<DetectorVerdict> ewmaVerdicts = DetectorSignals.Run(ewma, Channel, series);
        IReadOnlyList<DetectorVerdict> madVerdicts = DetectorSignals.Run(mad, Channel, series);

        ewmaVerdicts[^1].IsAnomaly.Should().BeTrue("the trained level is fixed, so a shift never becomes normal");
        madVerdicts[^1].IsAnomaly.Should().BeFalse(
            "once more than half the window sits at the new level, the new level is the median");

        // Measured, and sharper than expected: the robust detector flags exactly the first shifted
        // sample and then goes quiet. Once two shifted samples are in the window the MAD itself
        // widens, so a step is visible to it for about one sample rather than for half a window.
        madVerdicts.Skip(40).Take(6).Flagged().Should().Be(1,
            "the robust detector sees the shift arrive, and then almost immediately stops");
    }

    [Fact]
    public void Ewma_MissesTheSmallIsolatedOutlierThatMadCatches()
    {
        var series = new List<double>(DetectorSignals.Wobble(40, 50.0, 0.5));
        series.AddRange(DetectorSignals.Wobble(10, 50.0, 0.5));
        series[45] = 53.0; // one sample, three units out, then straight back

        var ewma = new EwmaLevelShiftDetector(trainingSamples: 40, lambda: 0.2, limitSigma: 3.0);
        var mad = new MedianAbsoluteDeviationDetector(window: 20, threshold: 3.5);

        DetectorSignals.Run(ewma, Channel, series).Flagged().Should().Be(0,
            "smoothing is what makes a level shift visible, and it is the same thing that hides a spike");
        DetectorSignals.Run(mad, Channel, series).Flagged().Should().Be(1,
            "the spike is four robust sigma from a median that has not moved");
    }

    // ---------------------------------------------------------------
    // Rate of change
    // ---------------------------------------------------------------

    [Fact]
    public void Rate_CatchesAStepOutOfAFlatLineThatBothStatisticalDetectorsDecline()
    {
        var series = new List<double>(DetectorSignals.Constant(30, 25.0)) { 45.0 };
        TimeSpan cadence = TimeSpan.FromMilliseconds(100);

        var rate = new RateOfChangeDetector(maxRatePerSecond: 50.0);
        var mad = new MedianAbsoluteDeviationDetector(window: 20, threshold: 3.5);
        var ewma = new EwmaLevelShiftDetector(trainingSamples: 20, lambda: 0.2, limitSigma: 3.0);

        DetectorVerdict rateVerdict = DetectorSignals.Run(rate, Channel, series, cadence)[^1];
        rateVerdict.HasVerdict.Should().BeTrue();
        rateVerdict.IsAnomaly.Should().BeTrue("20 units in 100 ms is 200 units/s against a 50 units/s limit");
        rateVerdict.Score.Should().BeApproximately(200.0, 1e-6);
        rateVerdict.ScoreKind.Should().Be(DetectorScoreKind.UnitsPerSecond);

        DetectorSignals.Run(mad, Channel, series, cadence)[^1].HasVerdict.Should().BeFalse();
        DetectorSignals.Run(ewma, Channel, series, cadence)[^1].HasVerdict.Should().BeFalse();
    }

    [Fact]
    public void Rate_MissesARampThatReachesExactlyTheSameValueMoreSlowly()
    {
        // 25 to 45 again, but one unit per 100 ms is 10 units/s and never touches the limit.
        double[] series = DetectorSignals.Ramp(21, 25.0, 1.0);

        IReadOnlyList<DetectorVerdict> verdicts = DetectorSignals.Run(
            new RateOfChangeDetector(maxRatePerSecond: 50.0), Channel, series, TimeSpan.FromMilliseconds(100));

        verdicts.Flagged().Should().Be(0, "a rate limit says nothing about where a channel ends up");
        verdicts[^1].HasVerdict.Should().BeTrue("it judged every step; it simply judged them all normal");
    }

    [Fact]
    public void Rate_DeclinesAcrossAGapRatherThanReportingAChangeAsSlow()
    {
        var detector = new RateOfChangeDetector(maxRatePerSecond: 50.0, maxGapSeconds: 5.0);

        detector.Evaluate(Channel, 25.0, DetectorSignals.Origin);
        DetectorVerdict afterGap = detector.Evaluate(Channel, 45.0, DetectorSignals.Origin.AddSeconds(60));

        afterGap.HasVerdict.Should().BeFalse("20 units over a minute of silence is not a measured rate");
        afterGap.Reason.Should().Contain("cannot be told from a step");
    }

    [Fact]
    public void Rate_DeclinesWhenSamplesShareATimestamp()
    {
        var detector = new RateOfChangeDetector(maxRatePerSecond: 50.0);

        detector.Evaluate(Channel, 25.0, DetectorSignals.Origin);
        DetectorVerdict same = detector.Evaluate(Channel, 45.0, DetectorSignals.Origin);

        same.HasVerdict.Should().BeFalse();
        same.Reason.Should().Contain("no interval to divide by");
    }

    // ---------------------------------------------------------------
    // The rule that governs all of them
    // ---------------------------------------------------------------

    [Fact]
    public void EveryDetectorWithholdsAVerdictBeforeItHasEnoughData_AndSaysWhy()
    {
        IChannelDetector[] detectors =
        {
            new MedianAbsoluteDeviationDetector(window: 20),
            new EwmaLevelShiftDetector(trainingSamples: 20),
            new RateOfChangeDetector(maxRatePerSecond: 5.0),
            new RollingZScoreDetector(window: 20)
        };

        foreach (IChannelDetector detector in detectors)
        {
            DetectorVerdict first = detector.Evaluate(Channel, 42.0, DetectorSignals.Origin);

            first.HasVerdict.Should().BeFalse($"{detector.DetectorId} has no baseline on its first sample");
            first.DetectorId.Should().BeNull("a null id is what distinguishes 'not judged' from 'judged calm'");
            first.Score.Should().Be(0.0);
            first.IsAnomaly.Should().BeFalse();
            first.Reason.Should().NotBeNullOrWhiteSpace($"{detector.DetectorId} must say why it declined");
        }
    }

    [Fact]
    public void EveryDetectorDeclinesANonFiniteSample_RatherThanScoringItAsAnExcursion()
    {
        IChannelDetector[] detectors =
        {
            new MedianAbsoluteDeviationDetector(window: 20),
            new EwmaLevelShiftDetector(trainingSamples: 20),
            new RateOfChangeDetector(maxRatePerSecond: 5.0),
            new RollingZScoreDetector(window: 20)
        };

        foreach (IChannelDetector detector in detectors)
        {
            DetectorSignals.Run(detector, Channel, DetectorSignals.Wobble(40, 10.0, 0.2));

            detector.Evaluate(Channel, double.NaN, DetectorSignals.Origin.AddMinutes(1))
                .HasVerdict.Should().BeFalse($"{detector.DetectorId} must not score a dropped reading");
        }
    }

    [Fact]
    public void EveryDetectorStampsItsOwnSettingsIntoItsIdentity()
    {
        new MedianAbsoluteDeviationDetector(window: 20, threshold: 3.5).DetectorId
            .Should().NotBe(new MedianAbsoluteDeviationDetector(window: 40, threshold: 3.5).DetectorId);

        new EwmaLevelShiftDetector(trainingSamples: 20, lambda: 0.2).DetectorId
            .Should().NotBe(new EwmaLevelShiftDetector(trainingSamples: 20, lambda: 0.4).DetectorId);

        new RateOfChangeDetector(maxRatePerSecond: 5.0).DetectorId
            .Should().NotBe(new RateOfChangeDetector(maxRatePerSecond: 6.0).DetectorId);

        new MedianAbsoluteDeviationDetector(label: "coolant").DetectorId
            .Should().StartWith("coolant:", "an operator's label must survive into the stored verdict");
    }
}
