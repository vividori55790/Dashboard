using TelemetryDashboard.Core.Analytics;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Noticing a channel that has stopped reporting, which no verdict about a value can.
/// </summary>
/// <remarks>
/// A dead sensor looks exactly like a steady one. The scope draws the last point it was given, the
/// statistics readout holds the mean it last computed, and the z-score sits at zero because the
/// distribution stopped moving too — so every surface on the desktop agreed that everything was
/// fine while the link was down.
/// <para>
/// Driven on the running application by stopping the virtual MCU stream: no banner while it ran,
/// "SIM:generic-machine.machine.speed has stopped reporting (6 s)" once it was stopped, silence
/// again after restarting it, and a second banner after stopping a second time. That last step is
/// what proves the recovery: a channel still flagged silent is skipped by the sweep and could never
/// have raised twice.
/// </para>
/// </remarks>
public class ChannelSilenceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static ChannelSilenceWatch Reporting(
        ChannelSilenceWatch watch, string channel, double everySeconds, int times, double from = 0)
    {
        for (int i = 0; i < times; i++) watch.Observe(channel, T0.AddSeconds(from + i * everySeconds));
        return watch;
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AChannelKeepingToItsOwnCadenceIsNeverCalledSilent()
    {
        var watch = new ChannelSilenceWatch { Factor = 5, MinimumSeconds = 5 };
        Reporting(watch, "COM3.temp", everySeconds: 0.05, times: 200);

        watch.Sweep(T0.AddSeconds(10.0)).Should().BeEmpty();
        watch.SilentChannels.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AChannelThatStopsIsReportedOnceAndNotOnEverySweep()
    {
        // A cable that has been out for an hour is one fault. Reporting it every second would be
        // an alarm per second for as long as it stayed out, which is how an alarm gets muted.
        var watch = new ChannelSilenceWatch { Factor = 5, MinimumSeconds = 5 };
        Reporting(watch, "COM3.temp", everySeconds: 0.05, times: 100);

        watch.Sweep(T0.AddSeconds(30)).Should().ContainSingle()
            .Which.Transition.Should().Be(SilenceTransition.WentSilent);

        watch.Sweep(T0.AddSeconds(31)).Should().BeEmpty();
        watch.Sweep(T0.AddSeconds(600)).Should().BeEmpty();
        watch.SilentChannels.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ReportingAgainEndsTheOutageAndTheChannelCanGoSilentAnew()
    {
        // The half a live run proves only by stopping the source twice: the second alarm cannot
        // happen unless the first outage was closed.
        var watch = new ChannelSilenceWatch { Factor = 5, MinimumSeconds = 5 };
        Reporting(watch, "COM3.temp", everySeconds: 0.05, times: 100);
        watch.Sweep(T0.AddSeconds(30)).Should().ContainSingle();

        watch.Observe("COM3.temp", T0.AddSeconds(40)).Should().Be(SilenceTransition.Returned);
        watch.SilentChannels.Should().Be(0);

        Reporting(watch, "COM3.temp", everySeconds: 0.05, times: 100, from: 40);
        watch.Sweep(T0.AddSeconds(120)).Should().ContainSingle("it must be able to fail twice");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheLengthOfAnOutageIsNotMistakenForTheChannelsCadence()
    {
        // The subtle one. If the gap that closed a silence were kept as the cadence, a channel that
        // dropped for an hour would need another hour of silence before anyone was told again --
        // the threshold would be raised by exactly the duration of the last fault.
        var watch = new ChannelSilenceWatch { Factor = 5, MinimumSeconds = 5 };
        Reporting(watch, "COM3.temp", everySeconds: 1.0, times: 20);
        watch.Sweep(T0.AddSeconds(3600)).Should().ContainSingle();

        watch.Observe("COM3.temp", T0.AddSeconds(3700));

        watch.ThresholdFor("COM3.temp").Should().BeApproximately(5.0, 1e-9,
            "one-second cadence at five times over, not the hour it was away");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheThresholdFollowsEachChannelsOwnCadence()
    {
        // A 20 Hz rail and a probe reporting once a minute are both healthy and both silent most
        // of the time. One number for the rig calls one of them dead and lets the other rot.
        var watch = new ChannelSilenceWatch { Factor = 5, MinimumSeconds = 5 };
        Reporting(watch, "fast", everySeconds: 0.05, times: 50);
        Reporting(watch, "slow", everySeconds: 60.0, times: 5);

        watch.ThresholdFor("fast").Should().Be(5.0, "the floor, not a quarter of a second");
        watch.ThresholdFor("slow").Should().BeApproximately(300.0, 1e-9);

        // Two minutes in, the fast channel is long gone and the slow one is merely between reports.
        var gone = watch.Sweep(T0.AddSeconds(60 * 4 + 120));
        gone.Select(g => g.Channel).Should().Contain("fast");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AFastChannelThatSkipsTwoFramesIsNotAFault()
    {
        var watch = new ChannelSilenceWatch { Factor = 5, MinimumSeconds = 5 };
        Reporting(watch, "COM3.temp", everySeconds: 0.05, times: 100);

        watch.Sweep(T0.AddSeconds(5.0 + 0.15)).Should().BeEmpty("three frames is 150 ms, not an outage");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AChannelSeenOnceIsJudgedAgainstTheFloorRatherThanNothing()
    {
        var watch = new ChannelSilenceWatch { Factor = 5, MinimumSeconds = 5 };
        watch.Observe("COM3.temp", T0);

        watch.Sweep(T0.AddSeconds(4)).Should().BeEmpty();
        watch.Sweep(T0.AddSeconds(6)).Should().ContainSingle();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ChangingSourceForgetsEverythingRatherThanReadingItAsAnOutage()
    {
        var watch = new ChannelSilenceWatch();
        Reporting(watch, "COM3.temp", everySeconds: 0.05, times: 50);

        watch.Reset();

        watch.TrackedChannels.Should().Be(0);
        watch.Sweep(T0.AddSeconds(3600)).Should().BeEmpty(
            "the previous rig's channels stopped because the operator changed rigs");
    }

    [Theory]
    [Trait("Category", "Tier2")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AReadingWithNoChannelNameIsNotTracked(string? channel)
    {
        var watch = new ChannelSilenceWatch();

        watch.Observe(channel!, T0).Should().Be(SilenceTransition.None);
        watch.TrackedChannels.Should().Be(0);
    }
}
