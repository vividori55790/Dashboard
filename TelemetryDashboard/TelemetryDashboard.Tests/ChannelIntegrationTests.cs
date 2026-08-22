using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.Core.Streaming;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// A channel that is the running total of another one, rather than a reading that drifts.
/// </summary>
/// <remarks>
/// State of charge is the number a UPS is bought for, and it is the one channel in this profile
/// that cannot be generated the way the others are. Declared as an ordinary channel it would wander
/// around its nominal at 8 % of its range — rising while the bank drains, falling while it charges —
/// and it would look exactly like every other trace on the screen. The previous simulator did a
/// gentler version of the same thing, <c>94.5 - t * 0.0005</c>, a ramp in wall-clock time that ran
/// at the same rate whether the bank was charging at +12 A or discharging at -32 A.
/// <para>
/// So these tests are not about a feature working. They are about the one property that separates a
/// coulomb count from a plausible-looking invention: the charge moves <em>because of the current</em>
/// and in the direction the current says. A test that only asserted "it changes" would pass on a
/// sign error, and a sign error here is a bank that reads as filling while it empties.
/// </para>
/// <para>
/// The whole class is in the measurement-sensitive collection. Every assertion below that runs the
/// engine is measuring wall-clock time — the integral advances by the seconds that actually passed,
/// not by the interval that was asked for — and two of those running beside each other measure each
/// other.
/// </para>
/// </remarks>
[Collection(HeavyTestCollection.Name)]
public class ChannelIntegrationTests
{
    private const string Charge = SimulatorChannelIds.UpsStateOfCharge;
    private const string Current = SimulatorChannelIds.UpsBatteryCurrent;

    /// <summary>The bundled UPS profile, read the way the host reads it.</summary>
    private static MonitoringProfile UpsProfile() =>
        MonitoringProfileStore.Load(AppContext.BaseDirectory).Profiles
            .Single(p => p.Id == "dab-psfb-ups");

    private static ProfileSimulatorEngine UpsEngine() => new(UpsProfile());

    private static NameValueCollection Query(params string[] pairs)
    {
        var query = new NameValueCollection();
        for (int i = 0; i + 1 < pairs.Length; i += 2) query[pairs[i]] = pairs[i + 1];
        return query;
    }

    // ---- what the bundled profile declares ----------------------------------

    [Fact]
    public void TheBundledProfileMakesTheChargeAnIntegralRatherThanAReading()
    {
        // The declaration itself, pinned. Everything below is arithmetic the engine does on these
        // numbers, so if the profile ever stops declaring the integration the rest of this file
        // would go on passing against a channel that is quietly drifting again.
        ProfileChannel charge = UpsProfile().Channels.Single(c => c.Id == Charge);

        charge.Integrates.Should().NotBeNull("state of charge is the integral of the battery current");
        charge.Integrates!.Source.Should().Be(Current);

        // 100 % / (200 Ah * 3600 s/h): one amp for one hour is 0.5 % of a 200 Ah bank.
        charge.Integrates.PerSecond.Should().BeApproximately(100.0 / (200.0 * 3600.0), 1e-15);

        charge.Minimum.Should().Be(0);
        charge.Maximum.Should().Be(100);
        charge.Nominal.Should().Be(92, "this is the value Reset returns the bank to");
    }

    // ---- the property the whole mechanism exists for ------------------------

    /// <summary>
    /// Runs the bundled profile on a discharge and checks the charge falls at the declared rate.
    /// </summary>
    /// <remarks>
    /// Slow because the quantity is slow: 180 A out of a 200 Ah bank is 1.5 % per minute, and the
    /// channel reports one decimal place, so a run short enough to be comfortable produces a fall
    /// that rounding could account for on its own. Twenty seconds gives about half a percent against
    /// a quantisation floor of a tenth, which is the smallest margin worth asserting on.
    /// <para>
    /// There is no way to step the engine by hand — <c>ProfileSimulatorEngine</c> exposes
    /// <c>StartSimulation</c> and the packet stream and nothing that advances one tick — so this
    /// measures real elapsed time and its bounds are wide enough to survive a loaded machine. What
    /// makes it more than a smoke test is the second assertion: the frames carry both the current
    /// and the charge with the same timestamps, so the total the engine produced can be recomputed
    /// from the stream it produced and compared with what it actually emitted.
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Category", "Tier2")]
    public async Task TheChargeFallsAtTheRateTheDeclaredCurrentImplies()
    {
        MonitoringProfile profile = UpsProfile();
        ChannelIntegration declared = profile.Channels.Single(c => c.Id == Charge).Integrates!;

        var engine = new ProfileSimulatorEngine(profile);
        engine.SetSetpoint(Current, -180).Should().Be(-180, "the outage scenario's discharge");

        List<TelemetryPacket> packets = await RunAsync(engine, TimeSpan.FromSeconds(20));

        List<TelemetryPacket> charge = Frames(packets, Charge);
        List<TelemetryPacket> current = Frames(packets, Current);

        charge.Should().HaveCountGreaterThan(100, "20 s at the default 10 Hz is about 200 frames");
        current.Should().OnlyContain(p => p.Value < 0,
            "the drift never carries a 180 A discharge back through zero, so every tick drains");

        double fell = charge[^1].Value - charge[0].Value;
        double seconds = (charge[^1].Timestamp - charge[0].Timestamp).TotalSeconds;

        charge.Select(p => p.Value).Should().BeInDescendingOrder(
            "a running total of a negative current never goes up; a drifting channel would, "
            + "and would do it by whole percent within a second");

        // The rate, against the number the profile's own description promises an operator: 1.5 %
        // per minute. Wide enough for a loaded machine and for the +/-25 A the drift puts on the
        // current, narrow enough that a rate wrong by a factor of two fails here.
        (fell / seconds * 60).Should().BeInRange(-2.1, -0.9,
            $"180 A out of a 200 Ah bank is 1.5 %/min; this run fell {fell:F2} % in {seconds:F1} s");

        // And the exact identity, recomputed from the same frames. The engine integrates the value
        // it emitted for the source, over the interval its own timestamps record, so the charge it
        // reported has to be the sum of the currents it reported -- not merely something that
        // moved the right way at roughly the right speed.
        Math.Abs(charge.Count - current.Count).Should().BeLessThanOrEqualTo(1,
            "both channels are emitted once per tick, so frame i of each is the same tick");

        double predicted = 0;
        for (int i = 1; i < charge.Count; i++)
        {
            predicted += current[i].Value * declared.PerSecond
                       * (charge[i].Timestamp - charge[i - 1].Timestamp).TotalSeconds;
        }

        predicted.Should().BeApproximately(fell, 0.15,
            "the charge is the integral of the current the frames actually carried");
    }

    [Fact]
    public async Task ACurrentIntoTheBankRaisesTheTotalAndACurrentOutOfItLowersIt()
    {
        // Both directions, because one of them alone proves nothing. A sign error produces a
        // channel that moves, moves smoothly, and moves by the right amount -- the only symptom is
        // that a bank reads as filling while an outage drains it, which is the reading an operator
        // would act on last.
        List<double> charging = await ChargeAsync(startAt: 20, current: 60, TimeSpan.FromSeconds(2));
        List<double> discharging = await ChargeAsync(startAt: 80, current: -180, TimeSpan.FromSeconds(2));

        charging.Should().HaveCountGreaterThan(4).And.BeInAscendingOrder();
        discharging.Should().HaveCountGreaterThan(4).And.BeInDescendingOrder();

        (charging[^1] - charging[0]).Should().BeGreaterThan(0.5,
            "a charging bank fills, and by more than the display could round into existence");
        (discharging[^1] - discharging[0]).Should().BeLessThan(-0.5,
            "a discharging bank empties");
    }

    [Fact]
    public async Task AFullBankStopsAtTheTopOfItsRangeAndAnEmptyOneAtTheBottom()
    {
        // A percentage that reads 102 % or -7 % is not a value an operator can discount as noise;
        // it is a reason to stop believing the channel. Both ends are clamped in the engine, and
        // without either the runs below would leave the range within the first second.
        List<double> full = await ChargeAsync(startAt: 100, current: 60, TimeSpan.FromSeconds(1));
        List<double> flat = await ChargeAsync(startAt: 0, current: -180, TimeSpan.FromSeconds(1));

        full.Should().NotBeEmpty().And.OnlyContain(v => v <= 100, "a full bank takes no more charge");
        full[^1].Should().BeApproximately(100, 1e-9);

        flat.Should().NotBeEmpty().And.OnlyContain(v => v >= 0, "an empty bank has nothing left to give");
        flat[^1].Should().BeApproximately(0, 1e-9);
    }

    [Fact]
    public async Task CommandingTheChargeRestartsTheRunningTotalFromThere()
    {
        // An integral has no setpoint to drift around, so commanding one means "the bank is at this
        // much now". Without the rebase the command would be accepted, reported as applied, and
        // change nothing that is ever emitted -- and it is what makes the 20 % low-charge alarm
        // provable in a minute rather than in the fifty it takes to discharge there.
        ProfileSimulatorEngine engine = UpsEngine();
        engine.SetSetpoint(Charge, 21).Should().Be(21);

        List<double> charge = Values(await RunAsync(engine, TimeSpan.FromSeconds(1)), Charge);

        charge.Should().NotBeEmpty();
        charge[0].Should().BeApproximately(21, 0.2, "the next reading starts from what was commanded");
        charge.Should().OnlyContain(v => v < 25,
            "nothing goes on reading 92 %; at the 4 A float nominal this barely moves in a second");
    }

    [Fact]
    public async Task ResetPutsTheBankBackAtTheNominalCharge()
    {
        ProfileSimulatorEngine engine = UpsEngine();
        engine.SetSetpoint(Charge, 21);

        engine.Reset();

        engine.GetSetpoint(Charge).Should().Be(92);

        // The setpoint alone is not the evidence: the accumulator is a separate store, and a Reset
        // that moved only the setpoint would leave the emitted charge sitting at 21 % on a rig the
        // operator had just returned to normal.
        List<double> charge = Values(await RunAsync(engine, TimeSpan.FromSeconds(1)), Charge);

        charge.Should().NotBeEmpty();
        charge[0].Should().BeApproximately(92, 0.2);
    }

    // ---- what the reader refuses to load ------------------------------------

    [Fact]
    public void AWellFormedDeclarationIsReadAndKept()
    {
        // The control for the three refusals below. Without it they could all be passing because
        // the hand-written JSON is malformed in some way that has nothing to do with integration.
        MonitoringProfileSet set = ReadProfileIntegrating("batt.current", perSecond: 0.5);

        set.Status.Should().Be(ProfileSourceStatus.Loaded);

        ChannelIntegration? read = set.Profiles.Single(p => p.Id == "bank-rig")
            .Channels.Single(c => c.Id == "batt.charge").Integrates;

        read.Should().NotBeNull();
        read!.Source.Should().Be("batt.current");
        read.PerSecond.Should().Be(0.5);
    }

    [Theory]
    [InlineData("batt.nosuch", 0.5, "batt.nosuch", "없는 채널")]
    [InlineData("batt.charge", 0.5, "batt.charge", "자기 자신")]
    [InlineData("batt.current", 0.0, "integralPerSecond", "0 이거나")]
    public void ADeclarationTheEngineCannotHonourIsRefusedAndNamed(
        string source, double perSecond, string named, string reason)
    {
        // All three fail the same way if they are not caught here: the channel loads, declares
        // itself an integral, and then holds still at its nominal for the life of the session. A
        // charge that never moves reads as a healthy bank, which is the one failure this whole
        // mechanism exists to avoid -- so the profile is refused at load rather than half-built.
        MonitoringProfileSet set = ReadProfileIntegrating(source, perSecond);

        set.Status.Should().Be(ProfileSourceStatus.Invalid);
        set.Profiles.Should().NotContain(p => p.Id == "bank-rig",
            "a profile whose channels do not validate must not load partly");

        set.Message.Should().Contain("batt.charge", "the message names the channel that is wrong")
            .And.Contain(named)
            .And.Contain(reason);
    }

    // ---- the scenarios that press the buttons -------------------------------

    [Fact]
    public void EveryScenarioTheProfileOffersDeclaresSomethingToApply()
    {
        // The regression guard on a control that reported success and moved nothing.
        // 'dab-overcurrent' and 'psfb-undervoltage' used to carry a Fault name and no setpoints.
        // Fault is read by the desktop shell alone, so on the headless host both resolved, looped
        // over zero setpoints and answered "Success" -- correct about every step they took, and
        // wrong about what happened.
        MonitoringProfile profile = UpsProfile();

        profile.Scenarios.Should().NotBeEmpty();
        profile.Scenarios.Should().OnlyContain(s => s.Setpoints.Count > 0,
            "a scenario states its effect in setpoints, because setpoints are the only thing the "
            + "headless host acts on");
    }

    [Theory]
    [InlineData("dab-overcurrent", SimulatorChannelIds.DabInputCurrent)]
    [InlineData("psfb-undervoltage", SimulatorChannelIds.PsfbOutputVoltage)]
    public void AScenarioPutsItsChannelWhereTheProfilesOwnLimitCallsItAFault(
        string scenarioId, string channelId)
    {
        // A button captioned "DAB overcurrent" that sets a current inside the safe band is a
        // demonstration of nothing: it moves a trace and raises no alarm, and the operator's
        // evidence that the alarm path works is that it did not fire. The setpoint is checked
        // against the profile's own limit rather than against a number written here, so the two
        // cannot drift apart.
        MonitoringProfile profile = UpsProfile();

        double setpoint = profile.Scenarios.Single(s => s.Id == scenarioId).Setpoints[channelId];
        ChannelLimit limit = profile.Limits.Select(ChannelLimit.Parse).Single(l => l.Channel == channelId);

        limit.IsBreached(setpoint).Should().BeTrue(
            $"'{scenarioId}' promises this alarm in its caption: {limit.Explain(setpoint)}");

        // And the slider has to reach that far, or the scenario would be clamped back inside the
        // band on its way in and breach nothing.
        ProfileSimulatorEngine engine = UpsEngine();
        engine.ApplyScenario(scenarioId).Should().BeEmpty();
        engine.GetSetpoint(channelId).Should().Be(setpoint);
    }

    [Theory]
    [InlineData(null, "A scenario states its effect in setpoints")]
    [InlineData("DabOvercurrent", "only the desktop shell")]
    public void ScenarioReportsAScenarioThatDeclaresNoSetpoints(string? fault, string expected)
    {
        // The reply an operator has to be able to act on. "Success" with an empty body is what this
        // endpoint used to answer for a scenario that applied nothing, and it is indistinguishable
        // from a scenario that worked: the evidence for either is a chart that did not change.
        var profile = new MonitoringProfile
        {
            Id = "rig",
            DisplayName = "Rig",
            Channels = [new ProfileChannel { Id = "load", Label = "Load", Minimum = 0, Maximum = 100, Nominal = 20 }],
            Scenarios = [new ProfileScenario { Id = "ghost", Label = "Ghost", Fault = fault }]
        };

        ControlEndpoint.Result result = ControlEndpoint.Apply(
            new ProfileSimulatorEngine(profile), Query("cmd", "scenario", "id", "ghost"));

        // Reported as a failure rather than as a success with a footnote. The request was
        // well formed and the scenario exists, so "the command was understood" is true and beside
        // the point: nothing moved, and a commissioning script that branches on Status would
        // otherwise go on to assert an alarm that was never given a chance to fire.
        result.Status.Should().Be("Error");
        result.Reason.Should().Contain("declares no setpoints")
            .And.Contain("nothing changed")
            .And.Contain(expected);
    }

    // ---- driving the engine -------------------------------------------------

    /// <summary>
    /// A two-channel bank whose charge moves fast enough to watch, so a run can be a second long.
    /// </summary>
    /// <remarks>
    /// The bundled profile's rate is the honest one for a 200 Ah bank and it is far too slow to
    /// assert on twice per test: at 60 A of charge the total moves a tenth of a percent per twelve
    /// seconds, which is the display's own resolution. This declares the same two channel ids over
    /// the same ranges with a rate 144 times larger, so direction and clamping — which are
    /// arithmetic in the engine and know nothing about any profile — can be proved in a second.
    /// The rate the bundled profile actually declares is asserted separately, on the bundled
    /// profile.
    /// </remarks>
    private static MonitoringProfile FastBank() => new()
    {
        Id = "fast-bank",
        DisplayName = "Fast bank",
        Nodes = [new ProfileNode { Id = "RIG_1", Label = "Rig" }],
        Channels =
        [
            new ProfileChannel
            {
                Id = Current, Label = "Battery current", Unit = "A",
                Minimum = -220, Maximum = 60, Nominal = 4, Decimals = 1
            },
            new ProfileChannel
            {
                Id = Charge, Label = "State of charge", Unit = "%",
                Minimum = 0, Maximum = 100, Nominal = 92, Decimals = 3,
                Integrates = new ChannelIntegration { Source = Current, PerSecond = 0.02 }
            }
        ]
    };

    /// <summary>Puts the fast bank at a charge and a current, runs it, and returns the charges.</summary>
    private static async Task<List<double>> ChargeAsync(double startAt, double current, TimeSpan span)
    {
        var engine = new ProfileSimulatorEngine(FastBank(), sampleRateHz: 20);
        engine.SetSetpoint(Charge, startAt);
        engine.SetSetpoint(Current, current);

        return Values(await RunAsync(engine, span), Charge);
    }

    /// <summary>
    /// Runs the engine for a wall-clock span and returns the frames it produced, parsed.
    /// </summary>
    /// <remarks>
    /// Through <see cref="DataRouter"/> rather than by reading the frame text, so a frame the
    /// production parser would reject simply never appears here — and so the timestamps these
    /// assertions measure against are the ones the engine stamped, not the ones this test observed
    /// the packet at. The span is wall clock rather than a frame count because a slow machine ticks
    /// more slowly and would otherwise run for proportionally longer, which for an integral means
    /// proportionally further.
    /// </remarks>
    private static async Task<List<TelemetryPacket>> RunAsync(ProfileSimulatorEngine engine, TimeSpan span)
    {
        var router = new DataRouter();
        router.RegisterRule(new RoutingRule
        {
            Id = "test", RuleType = RuleType.Prefix, Tag = "TELE", Port = "*", TargetNodeId = string.Empty
        });

        var packets = new List<TelemetryPacket>();
        DateTime? first = null;

        using var cancellation = new CancellationTokenSource(span + TimeSpan.FromSeconds(15));

        engine.StartSimulation();
        try
        {
            await foreach (RawPacket raw in engine.StreamSimulatedPackets(cancellation.Token))
            {
                first ??= raw.Timestamp;
                packets.AddRange(router.Route(raw));
                if (raw.Timestamp - first.Value >= span) break;
            }
        }
        catch (OperationCanceledException)
        {
            // The assertions report a short run better than an exception would.
        }
        finally
        {
            await engine.DisposeAsync();
        }

        return packets;
    }

    private static List<TelemetryPacket> Frames(IEnumerable<TelemetryPacket> packets, string channelId) =>
        packets.Where(p => p.Variable == channelId).ToList();

    private static List<double> Values(IEnumerable<TelemetryPacket> packets, string channelId) =>
        Frames(packets, channelId).Select(p => p.Value).ToList();

    /// <summary>
    /// Round-trips a two-channel profile through the JSON reader, which is where validation lives.
    /// </summary>
    /// <remarks>
    /// Hand-written JSON rather than a constructed <see cref="MonitoringProfile"/>, because a
    /// profile arriving as a file is the case the validation exists for: an object built in C#
    /// cannot name a channel that is not there.
    /// </remarks>
    private static MonitoringProfileSet ReadProfileIntegrating(string source, double perSecond)
    {
        string rate = perSecond.ToString("R", CultureInfo.InvariantCulture);
        string json = $$"""
            {
              "profiles": [
                {
                  "id": "bank-rig",
                  "displayName": "Bank rig",
                  "channels": [
                    { "id": "batt.current", "label": "Current", "unit": "A",
                      "minimum": -100, "maximum": 100, "nominal": 0, "decimals": 1 },
                    { "id": "batt.charge", "label": "Charge", "unit": "%",
                      "minimum": 0, "maximum": 100, "nominal": 50, "decimals": 1,
                      "integrates": "{{source}}", "integralPerSecond": {{rate}} }
                  ]
                }
              ]
            }
            """;

        string dir = Path.Combine(Path.GetTempPath(), "tdint_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, MonitoringProfileStore.FileName), json);
            return MonitoringProfileStore.Load(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
