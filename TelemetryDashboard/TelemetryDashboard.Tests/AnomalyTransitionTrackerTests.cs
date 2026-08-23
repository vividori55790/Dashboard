using TelemetryDashboard.Core.Analytics;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Turning a per-sample verdict into the two events worth telling somebody about.
/// </summary>
/// <remarks>
/// The shell had edge detection already, and its note promised "a channel that stays out of range
/// for a minute is one event, not two thousand four hundred". It is not, because the verdict it
/// watches is a bare threshold comparison and a reading hovering near the bar crosses it in both
/// directions every few samples. Measured on the running shell, one channel in four hundred
/// milliseconds: anomalous at z=2.78, recovered at 2.40, anomalous at 2.59, recovered at 2.30.
/// <para>
/// Four events and nothing changed about the machine. At that rate the event log — three hundred
/// rows, and where the silence watch, the limit alarms, the arming check and the link events all
/// deliver their answers — holds nothing else within seconds.
/// </para>
/// </remarks>
public class AnomalyTransitionTrackerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static AnomalyTransitionTracker Tracker(double calmSeconds = 5) =>
        new() { CalmBeforeClear = TimeSpan.FromSeconds(calmSeconds) };

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheOnsetIsAnnouncedAtOnce()
    {
        // An alarm that waits before announcing itself is an alarm that arrives late, and the two
        // mistakes do not cost the same.
        AnomalyTransitionTracker tracker = Tracker();

        tracker.Observe("rig.rail", isAnomaly: true, T0).Should().Be(AnomalyTransition.Entered);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AVerdictFlappingAcrossTheThresholdIsOneEvent()
    {
        // The measured case, replayed: four crossings in four hundred milliseconds.
        AnomalyTransitionTracker tracker = Tracker();

        var seen = new List<AnomalyTransition>
        {
            tracker.Observe("rig.rail", true, T0),
            tracker.Observe("rig.rail", false, T0.AddMilliseconds(185)),
            tracker.Observe("rig.rail", true, T0.AddMilliseconds(229)),
            tracker.Observe("rig.rail", false, T0.AddMilliseconds(386))
        };

        seen.Should().Equal(new[]
        {
            AnomalyTransition.Entered,
            AnomalyTransition.None,
            AnomalyTransition.None,
            AnomalyTransition.None
        });
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ARecoveryIsAnnouncedOnceTheChannelHasActuallyStayedNormal()
    {
        // The recovery is worth a line for the same reason the onset is: a log that shows an alarm
        // and never shows it clearing reads like an alarm that never cleared.
        AnomalyTransitionTracker tracker = Tracker();

        tracker.Observe("rig.rail", true, T0);
        tracker.Observe("rig.rail", false, T0.AddSeconds(1)).Should().Be(AnomalyTransition.None);
        tracker.Observe("rig.rail", false, T0.AddSeconds(4)).Should().Be(AnomalyTransition.None);
        tracker.Observe("rig.rail", false, T0.AddSeconds(6)).Should().Be(AnomalyTransition.Cleared);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void OneAnomalousSampleInTheMiddleOfCalmRestartsTheWait()
    {
        // Otherwise a channel that is still misbehaving, and merely reads normally once every few
        // seconds, accumulates its way to a recovery it has not earned.
        AnomalyTransitionTracker tracker = Tracker();

        tracker.Observe("rig.rail", true, T0);
        tracker.Observe("rig.rail", false, T0.AddSeconds(4));
        tracker.Observe("rig.rail", true, T0.AddSeconds(4.5)).Should().Be(AnomalyTransition.None,
            "it never left, so there is nothing to announce");

        tracker.Observe("rig.rail", false, T0.AddSeconds(8)).Should().Be(AnomalyTransition.None);
        tracker.Observe("rig.rail", false, T0.AddSeconds(10)).Should().Be(AnomalyTransition.Cleared);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AChannelThatWasNeverAnomalousAnnouncesNothing()
    {
        AnomalyTransitionTracker tracker = Tracker();

        tracker.Observe("rig.rail", false, T0).Should().Be(AnomalyTransition.None);
        tracker.Observe("rig.rail", false, T0.AddMinutes(5)).Should().Be(AnomalyTransition.None);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ChannelsAreTrackedApart()
    {
        AnomalyTransitionTracker tracker = Tracker();

        tracker.Observe("a.x", true, T0).Should().Be(AnomalyTransition.Entered);
        tracker.Observe("b.x", true, T0).Should().Be(AnomalyTransition.Entered);
        tracker.InAnomalyCount.Should().Be(2);

        tracker.Observe("a.x", false, T0.AddSeconds(6)).Should().Be(AnomalyTransition.Cleared);
        tracker.InAnomalyCount.Should().Be(1, "b is still out of range");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AResetForgetsEverythingRatherThanAnnouncingRecoveriesForAnotherRig()
    {
        AnomalyTransitionTracker tracker = Tracker();
        tracker.Observe("a.x", true, T0);

        tracker.Reset();

        tracker.InAnomalyCount.Should().Be(0);
        tracker.Observe("a.x", false, T0.AddSeconds(30)).Should().Be(AnomalyTransition.None,
            "the previous rig's excursion is not this one's recovery");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ASustainedExcursionIsStillOneEventNoMatterHowLongItLasts()
    {
        AnomalyTransitionTracker tracker = Tracker();

        tracker.Observe("rig.rail", true, T0).Should().Be(AnomalyTransition.Entered);

        for (int i = 1; i <= 600; i++)
        {
            tracker.Observe("rig.rail", true, T0.AddSeconds(i * 0.1))
                .Should().Be(AnomalyTransition.None);
        }
    }
}
