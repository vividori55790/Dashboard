using System;
using System.Linq;
using FluentAssertions;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Streaming;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Engineering limits: the alarm a rolling detector structurally cannot raise.
/// </summary>
/// <remarks>
/// A z-score asks how unusual a reading is against the channel's own recent history, so a bus that
/// settles above its ceiling and stays there becomes normal to it within a minute — the baseline
/// follows the fault in. Measured on a live host: 107 consecutive samples of a channel running
/// 42–119 V above a hard limit, every one scored, |z| never above 1.94, zero flagged. The limit
/// caught all 107.
/// </remarks>
public class ChannelLimitTests
{
    private static readonly DateTime At = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("dab.bus_voltage[V] in 370..420", "dab.bus_voltage", "V", 370.0, 420.0)]
    [InlineData("dab.bus_voltage in 370..420", "dab.bus_voltage", "", 370.0, 420.0)]
    [InlineData("temp[C] < 110", "temp", "C", null, 110.0)]
    [InlineData("temp[C] <= 110", "temp", "C", null, 110.0)]
    [InlineData("psfb.efficiency[%] > 85", "psfb.efficiency", "%", 85.0, null)]
    [InlineData("psfb.efficiency[%] >= 85", "psfb.efficiency", "%", 85.0, null)]
    public void ADeclarationCarriesItsChannelUnitAndBand(
        string declaration, string channel, string unit, double? min, double? max)
    {
        ChannelLimit limit = ChannelLimit.Parse(declaration);

        limit.Channel.Should().Be(channel);
        limit.Unit.Should().Be(unit);
        limit.Minimum.Should().Be(min);
        limit.Maximum.Should().Be(max);
    }

    [Theory]
    [InlineData("", "for example")]
    [InlineData("just a sentence", "is not a limit")]
    [InlineData("temp in 5", "is not a limit")]
    [InlineData("temp[C] in 420..380", "lower bound above its upper bound")]
    [InlineData("temp[C] < abc", "is not a limit")]
    public void AMalformedDeclarationIsRefusedWhereItIsWritten(string declaration, string expected)
    {
        Action parse = () => ChannelLimit.Parse(declaration);

        parse.Should().Throw<FormatException>().WithMessage($"*{expected}*");
    }

    [Fact]
    public void AnInvertedBandIsRefusedRatherThanReordered()
    {
        // Reordering is the tempting repair and it is wrong here: which number the author meant as
        // the ceiling is a guess about what is dangerous.
        Action parse = () => ChannelLimit.Parse("dab.bus_voltage[V] in 420..370");

        parse.Should().Throw<FormatException>()
            .WithMessage("*not something this can decide for you*");
    }

    [Theory]
    [InlineData(369.9, true)]
    [InlineData(370.0, false)]
    [InlineData(400.0, false)]
    [InlineData(420.0, false)]
    [InlineData(420.1, true)]
    public void TheBandIsInclusiveAtBothEnds(double value, bool breached)
    {
        ChannelLimit.Parse("v[V] in 370..420").IsBreached(value).Should().Be(breached);
    }

    [Fact]
    public void ANonFiniteReadingIsNotReportedAsAnExcursion()
    {
        // NaN and infinity are decode faults, which the parser layer surfaces as what they are.
        // Calling them process excursions sends an operator to the wrong end of the problem.
        ChannelLimit limit = ChannelLimit.Parse("v[V] in 370..420");

        limit.IsBreached(double.NaN).Should().BeFalse();
        limit.IsBreached(double.PositiveInfinity).Should().BeFalse();
    }

    [Fact]
    public void AUnitTheChannelDoesNotReportDisarmsTheRule()
    {
        ChannelLimit limit = ChannelLimit.Parse("v[kV] in 0.37..0.42");

        limit.UnitAgrees("kV").Should().BeTrue();
        limit.UnitAgrees("V").Should().BeFalse(
            "a limit in kV against a channel in volts never fires, and never firing has no symptom");
    }

    [Fact]
    public void ARuleWithNoUnitCannotDisagreeWithOne()
    {
        ChannelLimit limit = ChannelLimit.Parse("v in 370..420");

        limit.UnitAgrees("V").Should().BeTrue();
        limit.UnitAgrees(null).Should().BeTrue();
    }

    // ---- the monitor -------------------------------------------------------

    private static LimitMonitor Monitor(params string[] declarations) =>
        new(declarations.Select(ChannelLimit.Parse).ToList());

    [Fact]
    public void ARuleAppliesToEveryNodeReportingThatChannel()
    {
        // The right reading for a safety limit: a ceiling on bus voltage constrains every converter
        // that reports one, and a rule meant for a single device is written with the node in it.
        LimitMonitor monitor = Monitor("bus[V] in 370..420");

        monitor.Evaluate("SIM:COM3.bus", 460, "V", At).Should().ContainSingle();
        monitor.Evaluate("SIM:COM4.bus", 400, "V", At).Should().ContainSingle();

        monitor.Snapshot().Should().HaveCount(2, "state is per rule and per channel");
        monitor.Snapshot().Single(r => r.Channel == "SIM:COM3.bus").InBreach.Should().BeTrue();
        monitor.Snapshot().Single(r => r.Channel == "SIM:COM4.bus").InBreach.Should().BeFalse(
            "one node recovering must not clear the alarm on another");
    }

    [Fact]
    public void ACrossingIsAnnouncedOnceAndThenSustained()
    {
        // A line per sample during an excursion is how an alarm gets muted: a converter held above
        // its ceiling for a minute at 9 Hz is five hundred identical lines.
        LimitMonitor monitor = Monitor("bus[V] in 370..420");

        Transition(monitor, 400).Should().Be(LimitTransition.None);
        Transition(monitor, 460).Should().Be(LimitTransition.Entered);
        Transition(monitor, 470).Should().Be(LimitTransition.Sustained);
        Transition(monitor, 480).Should().Be(LimitTransition.Sustained);
        Transition(monitor, 400).Should().Be(LimitTransition.Cleared);
        Transition(monitor, 401).Should().Be(LimitTransition.None);

        LimitMonitor.RuleState state = monitor.Snapshot().Single();
        state.Entries.Should().Be(1, "one crossing, however long it lasted");
        state.Breaches.Should().Be(3, "and the duration is still countable");
        state.Evaluated.Should().Be(6);
    }

    [Fact]
    public void AMismatchedUnitIsItsOwnOutcomeRatherThanASilentPass()
    {
        LimitMonitor monitor = Monitor("bus[kV] in 0.37..0.42");

        Transition(monitor, 460, "V").Should().Be(LimitTransition.UnitMismatch);

        LimitMonitor.RuleState state = monitor.Snapshot().Single();
        state.Evaluated.Should().Be(0, "nothing was checked");
        state.InBreach.Should().BeFalse();
        state.UnitMismatch.Should().Contain("kV");
    }

    [Fact]
    public void ARuleNothingMatchesIsStillListed()
    {
        // A limit on a misspelled channel is silent, and so is a limit on a healthy one. Omitting
        // it would make the two indistinguishable, and the point of writing it down was protection.
        LimitMonitor monitor = Monitor("nowhere.at_all[V] < 5");

        monitor.Evaluate("SIM:COM3.bus", 400, "V", At).Should().BeEmpty();

        LimitMonitor.RuleState state = monitor.Snapshot().Should().ContainSingle().Subject;
        state.Channel.Should().Be("nowhere.at_all");
        state.Evaluated.Should().Be(0);
    }

    [Fact]
    public void TheEndpointSeparatesQuietFromUnprotected()
    {
        LimitMonitor monitor = Monitor(
            "bus[V] in 370..420",
            "bus[kV] in 0.37..0.42",
            "nowhere[V] < 5");

        monitor.Evaluate("SIM:COM3.bus", 400, "V", At);

        LimitsEndpoint.Result result = LimitsEndpoint.Query(monitor);

        result.Declared.Should().Be(3);
        result.Breached.Should().Be(0);
        result.Unarmed.Should().Be(2,
            "the number an operator needs before trusting a quiet alarm list");

        result.Rules.Select(r => r.Status).Should().BeEquivalentTo(
            new[] { "Watching", "Unarmed", "Never" });
    }

    [Fact]
    public void AHostCheckingNothingSaysSoRatherThanShowingACleanList()
    {
        LimitsEndpoint.Result result = LimitsEndpoint.Query(null);

        result.Status.Should().Be("Error");
        result.Reason.Should().Contain("--limit");
    }

    // ---- the ingest path ---------------------------------------------------

    [Fact]
    public async System.Threading.Tasks.Task ABreachedSampleIsFlaggedAndSaysSoOnTheWire()
    {
        var server = new TelemetryStreamingServer(port: 0) { Limits = Monitor("bus[V] in 370..420") };
        var publisher = new Host.Ingest.IngestPublisher(
            server, "TEST", isSimulated: false, recorder: null,
            new Host.Ingest.IngestRateGuard(0));

        var packet = new Core.Models.TelemetryPacket("COM3", "bus", 460, "V", DateTime.UtcNow);
        await publisher.PublishAsync(packet, "COM3", default);

        packet.Flags.HasFlag(Core.Models.PacketFlags.AlarmExceeded).Should().BeTrue();

        Host.Ingest.TelemetryFrame frame = Host.Ingest.TelemetryFrame.Create(
            packet, new Core.Analytics.AnomalyResult(), "TEST", simulated: false, "COM3");

        frame.LimitBreach.Should().BeTrue();
        frame.IsAnomaly.Should().BeNull(
            "one sample is no baseline; the limit fires anyway, which is the entire point");
    }

    [Fact]
    public async System.Threading.Tasks.Task AReadingInsideTheBandCarriesNoBreachField()
    {
        // Absent rather than false, so a consumer sees the field only when it means something.
        var server = new TelemetryStreamingServer(port: 0) { Limits = Monitor("bus[V] in 370..420") };
        var publisher = new Host.Ingest.IngestPublisher(
            server, "TEST", isSimulated: false, recorder: null,
            new Host.Ingest.IngestRateGuard(0));

        var packet = new Core.Models.TelemetryPacket("COM3", "bus", 400, "V", DateTime.UtcNow);
        await publisher.PublishAsync(packet, "COM3", default);

        Host.Ingest.TelemetryFrame.Create(
            packet, new Core.Analytics.AnomalyResult(), "TEST", false, "COM3")
            .LimitBreach.Should().BeNull();
    }

    [Fact]
    public void AProfilesSliderRangeIsNotItsAlarmBand()
    {
        // The distinction the whole feature rests on. The bundled profile's bus slider reaches
        // 450 V and its alarm ceiling is 420: that gap is how an over-voltage gets injected on
        // purpose. One pair of numbers for both would alarm on every deliberate test and on
        // nothing else.
        Core.Simulator.MonitoringProfile profile = Core.Simulator.MonitoringProfileStore
            .Load(AppContext.BaseDirectory).Profiles
            .Single(p => p.Id == "dab-psfb-ups");

        Core.Simulator.ProfileChannel bus =
            profile.Channels.Single(c => c.Id == "dab.bus_voltage");

        ChannelLimit limit = profile.Limits
            .Select(ChannelLimit.Parse)
            .Single(l => l.Channel == "dab.bus_voltage");

        bus.Maximum.Should().BeGreaterThan(limit.Maximum!.Value,
            "the slider has to be able to reach past the ceiling, or the fault cannot be injected");
        bus.Minimum.Should().BeLessThan(limit.Minimum!.Value);
    }

    private static LimitTransition Transition(LimitMonitor monitor, double value, string unit = "V") =>
        monitor.Evaluate("SIM:COM3.bus", value, unit, At).Single().Transition;
}
