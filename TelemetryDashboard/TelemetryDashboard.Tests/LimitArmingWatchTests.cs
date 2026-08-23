using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Telling "no alarms because the machine is fine" apart from "no alarms because nothing is judging".
/// </summary>
/// <remarks>
/// The browser console was given this answer from <c>/api/limits</c> a cycle earlier. The desktop
/// shell had no equivalent: it logged a breach, a recovery and a unit mismatch, and said nothing at
/// all about a band that had never matched a sample — which is what a channel name the device does
/// not use looks like, and what a rig commissioned in stages looks like.
/// <para>
/// Driven on the running shell as well: with the DAB/PSFB profile and its own simulator, all seven
/// bands report as judging; the interesting case is the other one, and it is what these pin.
/// </para>
/// </remarks>
public class LimitArmingWatchTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static LimitMonitor Monitor() =>
        LimitDeclarations.Resolve(MonitoringProfileLibrary.PowerConverterUps.Limits).Monitor!;

    private static LimitArmingWatch Watching(TimeSpan grace)
    {
        var watch = new LimitArmingWatch { Grace = grace };
        watch.Reset(Start);
        return watch;
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ABandNothingEverMatchedIsNamedAlongWithTheChannelItWants()
    {
        // Naming the channel the band is waiting for is what turns "no alarms" into a one-line fix:
        // nine times in ten the device is sending the same quantity under its own name.
        LimitArmingWatch watch = Watching(TimeSpan.Zero);

        IReadOnlyList<string> lines = watch.Sweep(Monitor().Snapshot(), Start);

        lines.Should().Contain(l => l.Contains("psfb.output_voltage")
                                    && l.Contains("한 번도 판정하지 않았습니다"));
        lines.Should().Contain(l => l.Contains("이름 매핑"), "the fix is named, not only the fault");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ItSaysSoOnceRatherThanOnEverySweep()
    {
        // It runs on a five-second timer. A line per band per sweep would bury the event log within
        // a minute, and a line somebody has learned to skip is a line that is no longer there.
        LimitArmingWatch watch = Watching(TimeSpan.Zero);
        LimitMonitor monitor = Monitor();

        watch.Sweep(monitor.Snapshot(), Start).Should().NotBeEmpty();
        watch.Sweep(monitor.Snapshot(), Start.AddSeconds(5)).Should().BeEmpty();
        watch.Sweep(monitor.Snapshot(), Start.AddSeconds(10)).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ABandThatIsJudgingIsNotAccused()
    {
        LimitArmingWatch watch = Watching(TimeSpan.Zero);
        LimitMonitor monitor = Monitor();

        // One reading on one channel is all it takes for that band to be armed.
        monitor.Evaluate("SIM.psfb.output_voltage", 48.0, "V", Start);

        IReadOnlyList<string> lines = watch.Sweep(monitor.Snapshot(), Start);

        lines.Should().NotContain(l => l.Contains("psfb.output_voltage[V]") && l.Contains("한 번도"));
        lines.Should().Contain(l => l.Contains("만 판정 중입니다"), "the others still are not");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ItConfirmsProtectionRatherThanOnlyReportingFaults()
    {
        // "All seven are judging" is the answer somebody wants before leaving a rig unattended, and
        // a screen that only ever speaks up about faults cannot give it.
        MonitoringProfile profile = MonitoringProfileLibrary.PowerConverterUps;
        LimitArmingWatch watch = Watching(TimeSpan.Zero);
        LimitMonitor monitor = Monitor();

        foreach (string declaration in profile.Limits)
        {
            string channel = declaration.Split('[')[0].Trim();
            string unit = profile.Channels.FirstOrDefault(c => c.Id == channel)?.Unit ?? string.Empty;
            monitor.Evaluate("SIM." + channel, 0.0, unit, Start);
        }

        watch.Sweep(monitor.Snapshot(), Start).Should().ContainSingle()
            .Which.Should().Contain("모두 판정 중입니다");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void NothingIsSaidBeforeTheGraceHasPassed()
    {
        // Reported at the moment a profile is applied it would name every band, because nothing has
        // arrived yet -- which is true and useless, and teaches the operator to ignore the line.
        LimitArmingWatch watch = Watching(TimeSpan.FromSeconds(30));
        LimitMonitor monitor = Monitor();

        watch.Sweep(monitor.Snapshot(), Start.AddSeconds(29)).Should().BeEmpty();
        watch.Sweep(monitor.Snapshot(), Start.AddSeconds(31)).Should().NotBeEmpty();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AProfileDeclaringNoBandsSaysNothingAtAll()
    {
        // A line about nothing, in a log whose entire value is that its lines matter.
        LimitArmingWatch watch = Watching(TimeSpan.Zero);

        watch.Sweep(null, Start).Should().BeEmpty();
        watch.Sweep([], Start).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AProfileChangeStartsTheClockAgain()
    {
        // The bands of a rig somebody has just selected have had no chance to see anything.
        LimitArmingWatch watch = Watching(TimeSpan.FromSeconds(30));
        LimitMonitor monitor = Monitor();

        watch.Sweep(monitor.Snapshot(), Start.AddSeconds(31)).Should().NotBeEmpty();

        watch.Reset(Start.AddMinutes(10));
        watch.Sweep(monitor.Snapshot(), Start.AddMinutes(10).AddSeconds(5)).Should().BeEmpty();
        watch.Sweep(monitor.Snapshot(), Start.AddMinutes(11)).Should().NotBeEmpty(
            "and it says it all again for the new rig");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ABandThatCannotJudgeBecauseOfItsUnitIsLeftToTheUnitWarning()
    {
        // A unit mismatch is already reported, once, by the path that meets it. Saying "never
        // judged" about the same rule would be the same fault twice under two names, and the second
        // one names the wrong fix.
        LimitArmingWatch watch = Watching(TimeSpan.Zero);
        LimitMonitor monitor = Monitor();

        monitor.Evaluate("SIM.psfb.output_voltage", 48000.0, "mV", Start);

        watch.Sweep(monitor.Snapshot(), Start)
            .Should().NotContain(l => l.Contains("psfb.output_voltage[V]") && l.Contains("한 번도"));
    }
}
