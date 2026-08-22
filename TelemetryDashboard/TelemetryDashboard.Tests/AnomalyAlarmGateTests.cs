using System;
using TelemetryDashboard.Core.Analytics;

namespace TelemetryDashboard.Tests;

/// <summary>
/// When a scored channel is worth interrupting an operator about.
/// </summary>
/// <remarks>
/// The desktop had two paths that scored a channel. One compared the z-score against a literal and
/// wrote a log line; the other computed a verdict and discarded it, so an anomaly on real hardware
/// produced nothing at all. Neither raised anything the operator would see while looking at the
/// chart.
/// <para>
/// Wiring an alarm surface to the engine's own <c>IsAnomaly</c> made that worse in a way worth
/// recording: it is a 2.5 sigma detection bar meant for marking a chart, and on a running window it
/// put a banner up on an idle machine within seconds and then replaced it on every sample.
/// </para>
/// </remarks>
public class AnomalyAlarmGateTests
{
    private static AnomalyResult Scored(double value, double mean, double sigma) => new()
    {
        ChannelName = "COM3.Temp",
        CurrentValue = value,
        Mean = mean,
        StdDev = 1.0,
        ZScore = sigma,
        SampleCount = 60,
        // A verdict exists only when the engine stamped its identity on the result. Without this
        // the gate treats the reading as warm-up and refuses to judge it.
        AnalyzerId = "test-analyzer"
    };

    [Fact]
    [Trait("Category", "Tier1")]
    public void AnExcursionIsAnnouncedOnceRatherThanOncePerSample()
    {
        // The shape of the defect this exists to prevent. A genuine excursion lasting ten seconds
        // at 20 Hz is one event and two hundred samples, and an alarm surface driven per sample
        // spends the incident redrawing itself.
        var gate = new AnomalyAlarmGate();

        gate.Evaluate("COM3.Temp", Scored(60, 24, 22.9)).Should().Be(AlarmTransition.Entered);
        gate.Evaluate("COM3.Temp", Scored(61, 24, 23.4)).Should().Be(AlarmTransition.Sustained);
        gate.Evaluate("COM3.Temp", Scored(62, 24, 24.0)).Should().Be(AlarmTransition.Sustained);
        gate.AlarmingCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ComingBackInsideTheBandIsReportedSoTheBannerCanBeTakenDown()
    {
        var gate = new AnomalyAlarmGate();
        gate.Evaluate("COM3.Temp", Scored(60, 24, 22.9));

        gate.Evaluate("COM3.Temp", Scored(24.2, 24, 0.2)).Should().Be(AlarmTransition.Cleared);
        gate.IsAlarming("COM3.Temp").Should().BeFalse();
        gate.Evaluate("COM3.Temp", Scored(24.1, 24, 0.1)).Should().Be(AlarmTransition.None);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheBarToRaiseIsHigherThanTheBarToStopRaising()
    {
        // Without the gap, a channel sitting on the threshold alternates between alarming and clear
        // on consecutive samples, and the operator is shown an alarm that flickers several times a
        // second -- worse than either state held steadily, because it also fills the log.
        var gate = new AnomalyAlarmGate { EnterSigma = 3.5, ClearSigma = 2.5 };
        gate.Evaluate("c", Scored(60, 24, 4.0)).Should().Be(AlarmTransition.Entered);

        gate.Evaluate("c", Scored(58, 24, 3.0)).Should().Be(AlarmTransition.Sustained,
            "three sigma is below the raise bar and above the clear bar, so nothing changes");
        gate.Evaluate("c", Scored(30, 24, 2.4)).Should().Be(AlarmTransition.Cleared);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AStillChannelThatWobblesIsNotAnAlarmHoweverManySigmaItIs()
    {
        // Measured on the running application: the ambient temperature raised an alarm reading
        // "23.98, 22.8 sigma" on an idle machine, while the engine's own readout for the same
        // channel showed a mean of 23.7 and a standard deviation of 2.47 -- which puts that value
        // 0.11 sigma from the middle. Both numbers were right. A z-score is a ratio, and during a
        // quiet window its denominator collapses.
        var gate = new AnomalyAlarmGate();

        gate.Evaluate("COM3.Temp", Scored(value: 23.98, mean: 23.7, sigma: 22.8))
            .Should().Be(AlarmTransition.None,
                "the reading is one percent away from its own baseline, whatever the ratio says");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ARealExcursionStillPassesTheSameGuard()
    {
        // The other half of the previous test: the guard must not be so blunt that it swallows the
        // event it was added around. 24 to 60 degrees is 150 % of the baseline.
        var gate = new AnomalyAlarmGate();

        gate.Evaluate("COM3.Temp", Scored(value: 60.0, mean: 24.0, sigma: 22.9))
            .Should().Be(AlarmTransition.Entered);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AChannelThatFallsAlarmsJustAsOneThatRises()
    {
        // The engine's own IsAnomaly compares the signed z-score against its threshold, so a
        // channel collapsing to zero is never flagged. A rail dropping out is the excursion an
        // operator most wants to hear about.
        var gate = new AnomalyAlarmGate();

        gate.Evaluate("psfb.output_voltage", Scored(value: 0.0, mean: 48.0, sigma: -19.0))
            .Should().Be(AlarmTransition.Entered);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void WarmUpIsNotEvidenceThatEverythingIsFine()
    {
        // During warm-up the engine reports no verdict and a zero z-score. Reading that as "below
        // the clear threshold" would silently take down an alarm that is still true, every time the
        // analyser restarted.
        var gate = new AnomalyAlarmGate();
        gate.Evaluate("c", Scored(60, 24, 22.9)).Should().Be(AlarmTransition.Entered);

        var warmingUp = new AnomalyResult { ChannelName = "c", CurrentValue = 60, Mean = 60 };
        warmingUp.HasVerdict.Should().BeFalse();

        gate.Evaluate("c", warmingUp).Should().Be(AlarmTransition.Sustained);
        gate.IsAlarming("c").Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ChannelsAreJudgedSeparately()
    {
        var gate = new AnomalyAlarmGate();

        gate.Evaluate("a", Scored(60, 24, 22.9)).Should().Be(AlarmTransition.Entered);
        gate.Evaluate("b", Scored(24.1, 24, 0.1)).Should().Be(AlarmTransition.None);

        gate.IsAlarming("a").Should().BeTrue();
        gate.IsAlarming("b").Should().BeFalse();
        gate.AlarmingCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ResettingForgetsEveryChannelSoTheNextReadingCanRaiseAgain()
    {
        var gate = new AnomalyAlarmGate();
        gate.Evaluate("a", Scored(60, 24, 22.9));

        gate.Reset();

        gate.AlarmingCount.Should().Be(0);
        gate.Evaluate("a", Scored(60, 24, 22.9)).Should().Be(AlarmTransition.Entered);
    }

    [Theory]
    [Trait("Category", "Tier1")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AReadingWithNoChannelNameIsNotAnAlarm(string? channel)
    {
        new AnomalyAlarmGate().Evaluate(channel!, Scored(60, 24, 22.9))
            .Should().Be(AlarmTransition.None);
    }
}
