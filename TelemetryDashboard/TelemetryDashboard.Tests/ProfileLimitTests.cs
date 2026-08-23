using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The safe bands a profile declares, and whether anything can act on them.
/// </summary>
/// <remarks>
/// Parsing lived in the host, so the desktop shell — the thing an engineer is actually sitting in
/// front of at a bench — could not reach it and evaluated no limits at all. It loaded a profile
/// stating the rig's bands, drew every channel, and compared nothing against them.
/// <para>
/// Driven on the running window: with the converter profile selected the shell logs "안전 밴드
/// 7개를 감시합니다", and dragging PSFB output voltage to 42 raised the banner
/// "SIM:COM3.psfb.output_voltage: 43.29 is below the 45 floor" beside a log line quoting the rule
/// it broke.
/// </para>
/// </remarks>
public class ProfileLimitTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void EveryBandTheConverterProfileDeclaresCanBeActedOn()
    {
        // A declaration that does not parse is dropped, and a rule that never fires looks exactly
        // like a machine that is behaving. The shipped profile's bands have to be usable.
        LimitDeclarations.Resolution resolved =
            LimitDeclarations.Resolve(MonitoringProfileLibrary.PowerConverterUps.Limits);

        resolved.Warnings.Should().BeEmpty();
        resolved.Monitor.Should().NotBeNull();
        resolved.Monitor!.Rules.Should().HaveCount(MonitoringProfileLibrary.PowerConverterUps.Limits.Count);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ABandTheParserCannotReadIsReportedRatherThanTakingTheOthersWithIt()
    {
        LimitDeclarations.Resolution resolved = LimitDeclarations.Resolve(new[]
        {
            "psfb.output_voltage[V] in 45..51",
            "this is not a limit",
            "dab.input_current[A] < 36"
        });

        resolved.Monitor!.Rules.Should().HaveCount(2, "one bad clause must not silence the rest");
        resolved.Warnings.Should().ContainSingle().Which.Should().Contain("this is not a limit");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheSameBandDeclaredTwiceIsOneRule()
    {
        // Otherwise a repeated declaration is two rules watching one band, announcing every
        // excursion twice.
        LimitDeclarations.Resolution resolved = LimitDeclarations.Resolve(new[]
        {
            "psfb.output_voltage[V] in 45..51",
            "psfb.output_voltage[V] in 45..51"
        });

        resolved.Monitor!.Rules.Should().ContainSingle();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AProfileThatDeclaresNoBandsGetsNoMonitorRatherThanAnEmptyOne()
    {
        // Null is what lets a caller say "this profile declares no bands, so no limit alarm will
        // sound" instead of implying a watch that is watching nothing.
        LimitDeclarations.Resolve(null).Monitor.Should().BeNull();
        LimitDeclarations.Resolve(MonitoringProfileLibrary.Generic.Limits).Monitor
            .Should().BeNull("the generic profile declares none");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ABandIsMatchedAgainstAChannelThatCarriesItsNodePrefix()
    {
        // The shell names channels SIM:COM3.psfb.output_voltage and the declaration names
        // psfb.output_voltage. A rule that failed to match would never fire, which has no symptom.
        LimitMonitor monitor = LimitDeclarations.Resolve(new[] { "psfb.output_voltage[V] in 45..51" }).Monitor!;

        var outcomes = monitor.Evaluate("SIM:COM3.psfb.output_voltage", 43.29, "V", DateTime.UtcNow);

        outcomes.Should().ContainSingle();
        outcomes[0].Transition.Should().Be(LimitTransition.Entered);
        outcomes[0].Rule.Explain(43.29).Should().Contain("45");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ABandCannotJudgeAChannelReportingADifferentUnit()
    {
        // Reported rather than skipped: a limit in the wrong unit never fires, and an alarm that
        // cannot fire is indistinguishable from a healthy machine.
        LimitMonitor monitor = LimitDeclarations.Resolve(new[] { "psfb.output_voltage[V] in 45..51" }).Monitor!;

        var outcomes = monitor.Evaluate("SIM:COM3.psfb.output_voltage", 43.29, "kV", DateTime.UtcNow);

        outcomes.Should().ContainSingle();
        outcomes[0].Transition.Should().Be(LimitTransition.UnitMismatch);
    }
}
