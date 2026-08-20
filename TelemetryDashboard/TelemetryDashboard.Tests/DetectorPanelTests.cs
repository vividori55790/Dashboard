using TelemetryDashboard.Core.Analytics.Detectors;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Several detectors over one channel, and the property that makes that worth doing: their answers
/// stay apart.
/// </summary>
public class DetectorPanelTests
{
    private const string Channel = "NODE_1.TEMP";

    /// <summary>A detector that fails, to prove one bad entry cannot take the ingest path with it.</summary>
    private sealed class ThrowingDetector : IChannelDetector
    {
        public string DetectorId => "broken/always-throws";
        public bool CanHandle(string channelName) => true;
        public void Reset(string channelName) { }
        public DetectorVerdict Evaluate(string channelName, double value, DateTime observedUtc) =>
            throw new InvalidOperationException("misconfigured");
    }

    [Fact]
    public void ThreeDetectorsOverOneChannelProduceThreeVerdictsThatCanBeToldApart()
    {
        var panel = new DetectorPanel(new IChannelDetector[]
        {
            new MedianAbsoluteDeviationDetector(window: 20, label: "robust"),
            new EwmaLevelShiftDetector(trainingSamples: 20, label: "level"),
            new RateOfChangeDetector(maxRatePerSecond: 5.0, label: "physical")
        });

        DateTime at = DetectorSignals.Origin;
        IReadOnlyList<DetectorVerdict> verdicts = Array.Empty<DetectorVerdict>();

        foreach (double value in DetectorSignals.Wobble(40, 10.0, 0.2))
        {
            verdicts = panel.Evaluate(Channel, value, at);
            at += TimeSpan.FromMilliseconds(100);
        }

        verdicts.Should().HaveCount(3);
        verdicts.Select(v => v.DetectorId).Should().OnlyHaveUniqueItems();
        verdicts.Select(v => v.DetectorId!).Should().AllSatisfy(id => id.Should().NotBeNullOrWhiteSpace());
        panel.Tallies.Should().HaveCount(3);
        panel.Tallies.Should().AllSatisfy(t => t.Offered.Should().Be(40));
    }

    [Fact]
    public void APanelRefusesTwoDetectorsThatWouldAnswerUnderTheSameIdentity()
    {
        Action build = () => new DetectorPanel(new IChannelDetector[]
        {
            new MedianAbsoluteDeviationDetector(window: 20, threshold: 3.5),
            new MedianAbsoluteDeviationDetector(window: 20, threshold: 3.5)
        });

        build.Should().Throw<ArgumentException>()
            .WithMessage("*could not be told apart*");
    }

    [Fact]
    public void TwoDetectorsOfTheSameKindAtDifferentSettingsAreAllowedAndDistinguishable()
    {
        var panel = new DetectorPanel(new IChannelDetector[]
        {
            new MedianAbsoluteDeviationDetector(window: 20, threshold: 3.5, label: "fast"),
            new MedianAbsoluteDeviationDetector(window: 60, threshold: 3.5, label: "slow")
        });

        panel.Detectors.Select(d => d.DetectorId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ADetectorIsOnlyAskedAboutChannelsItWasPointedAt()
    {
        var panel = new DetectorPanel(new IChannelDetector[]
        {
            new MedianAbsoluteDeviationDetector(window: 20, channels: new ChannelSelector(new[] { "*.TEMP" }))
        });

        panel.Evaluate("NODE_1.TEMP", 1.0, DetectorSignals.Origin).Should().HaveCount(1);
        panel.Evaluate("NODE_1.PRESSURE", 1.0, DetectorSignals.Origin).Should().BeEmpty(
            "a channel nobody is watching must be absent from the answers, not present with a false calm");
    }

    [Fact]
    public void ADetectorThatThrowsIsContainedAndCountedAsHavingWithheldAVerdict()
    {
        var panel = new DetectorPanel(new IChannelDetector[] { new ThrowingDetector() });

        IReadOnlyList<DetectorVerdict> verdicts = panel.Evaluate(Channel, 1.0, DetectorSignals.Origin);

        verdicts.Should().HaveCount(1);
        verdicts[0].HasVerdict.Should().BeFalse();
        verdicts[0].Reason.Should().Contain("detector threw");
        panel.Tallies[0].Withheld.Should().Be(1);
    }

    [Fact]
    public void ADetectorThatNeverJudgedAnythingSaysSoInTheSummary()
    {
        var panel = new DetectorPanel(new IChannelDetector[]
        {
            new MedianAbsoluteDeviationDetector(window: 60)
        });

        foreach (double value in DetectorSignals.Constant(30, 7.0))
        {
            panel.Evaluate(Channel, value, DetectorSignals.Origin);
        }

        panel.VerdictsReached.Should().Be(0);
        panel.Summary().Single().Should().Contain("never judged anything")
            .And.Contain("perfectly constant",
                "an operator reading an empty alert list must be able to tell 'nothing was wrong' "
                + "from 'this detector was never able to answer'");
    }

    [Fact]
    public void FlaggedVerdictsAreRetainedPerChannelAndPerDetector()
    {
        var panel = new DetectorPanel(new IChannelDetector[]
        {
            new MedianAbsoluteDeviationDetector(window: 20, threshold: 3.5, label: "robust"),
            new RateOfChangeDetector(maxRatePerSecond: 20.0, label: "physical")
        });

        DateTime at = DetectorSignals.Origin;
        var series = new List<double>(DetectorSignals.Wobble(20, 10.0, 0.1)) { 100.0 };

        foreach (double value in series)
        {
            panel.Evaluate(Channel, value, at);
            at += TimeSpan.FromMilliseconds(100);
        }

        IReadOnlyList<FlaggedVerdict> flags = panel.RecentFlags;

        flags.Should().HaveCount(2, "both detectors flagged the same sample, and both records are kept");
        flags.Select(f => f.Verdict.DetectorId).Should().OnlyHaveUniqueItems();
        flags.Should().AllSatisfy(f => f.Channel.Should().Be(Channel));
        flags.Should().AllSatisfy(f => f.Value.Should().Be(100.0));
        flags.Select(f => f.Verdict.ScoreKind).Should()
            .Contain(DetectorScoreKind.RobustSigma).And.Contain(DetectorScoreKind.UnitsPerSecond,
                "the scales differ, so each verdict has to carry its own");
    }

    [Fact]
    public void AnEmptyPanelReportsNoVerdictsRatherThanNoAnomalies()
    {
        DetectorPanel panel = DetectorPanel.Empty;

        panel.IsEmpty.Should().BeTrue();
        panel.Evaluate(Channel, 1.0, DetectorSignals.Origin).Should().BeEmpty();
        panel.VerdictsReached.Should().Be(0);
        panel.AnomaliesFlagged.Should().Be(0);
        panel.Summary().Should().BeEmpty();
    }

    [Theory]
    [InlineData("*", "NODE_1.TEMP", true)]
    [InlineData("*.TEMP", "NODE_1.TEMP", true)]
    [InlineData("*.TEMP", "NODE_1.TEMPERATURE", false)]
    [InlineData("NODE_?.TEMP", "NODE_9.TEMP", true)]
    [InlineData("NODE_?.TEMP", "NODE_10.TEMP", false)]
    [InlineData("SIM:*", "SIM:MCU_A.V", true)]
    [InlineData("a*a*a*a*b", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaac", false)]
    public void ChannelPatternsMatchWhatAnOperatorWouldExpect(string pattern, string channel, bool expected)
    {
        new ChannelSelector(new[] { pattern }).Matches(channel).Should().Be(expected);
    }

    [Fact]
    public void AnEmptyChannelSelectorMatchesNothing_NotEverything()
    {
        new ChannelSelector(Array.Empty<string>()).Matches("NODE_1.TEMP").Should().BeFalse(
            "a channel list that failed to load must not silently attach a detector to the whole plant");
    }
}
