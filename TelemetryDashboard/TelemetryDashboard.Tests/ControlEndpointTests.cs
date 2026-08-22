using System;
using System.Collections.Specialized;
using System.Linq;
using FluentAssertions;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.Core.Streaming;
using TelemetryDashboard.Host.Startup;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// <c>/api/control</c>: the one place the cross-platform product is not read-only.
/// </summary>
/// <remarks>
/// A browser could watch, query and be alerted, and could change nothing. The streaming server even
/// raised a <c>CommandReceived</c> event for text arriving on the WebSocket and nothing subscribed
/// to it, so a command sent from a console was raised and dropped — a control that appears to work
/// and changes nothing, which is worse than one that is visibly absent.
/// <para>
/// What that cost is commissioning: proving the alarm fires and the interlock trips, without
/// over-volting real hardware. Verified end to end on a live host — one POST moved the bus to
/// 440 V, the limit reported 210 breaching samples, the interlock dispatched five times, SAFE_MODE
/// reached the port and Slack received the crossing and then the recovery.
/// </para>
/// </remarks>
public class ControlEndpointTests
{
    private static ProfileSimulatorEngine Engine() =>
        new(MonitoringProfileStore.Load(AppContext.BaseDirectory).Profiles
            .Single(p => p.Id == "dab-psfb-ups"));

    private static NameValueCollection Query(params string[] pairs)
    {
        var query = new NameValueCollection();
        for (int i = 0; i + 1 < pairs.Length; i += 2) query[pairs[i]] = pairs[i + 1];
        return query;
    }

    [Fact]
    public void AHostWithNothingToCommandSaysSoRatherThanAcceptingTheCommand()
    {
        // The enforcement is that there is no object, not a check that refuses. A host reading a
        // real device is read-only here on purpose: moving that machine is the interlock's job.
        ControlEndpoint.Result result =
            ControlEndpoint.Apply(null, Query("cmd", "setpoint", "channel", "x", "value", "1"));

        result.Status.Should().Be("Error");
        result.Reason.Should().Contain("--simulate").And.Contain("interlock");
    }

    [Fact]
    public void TheEndpointListsWhatMayBeCommandedAndWhereEachChannelSits()
    {
        // Discoverable without documentation: a caller that has to guess channel ids will guess.
        ControlEndpoint.Result described = ControlEndpoint.Describe(Engine());

        described.Channels.Select(c => c.Id).Should().Contain("dab.bus_voltage");
        described.Scenarios.Select(s => s.Id).Should().Contain("grid-outage");

        ControlEndpoint.ChannelState bus = described.Channels.Single(c => c.Id == "dab.bus_voltage");
        bus.Setpoint.Should().Be(bus.Nominal, "nothing has moved it yet");
        bus.Unit.Should().Be("V");
    }

    [Fact]
    public void ASetpointIsAppliedAndReadBack()
    {
        ProfileSimulatorEngine engine = Engine();

        ControlEndpoint.Result result = ControlEndpoint.Apply(
            engine, Query("cmd", "setpoint", "channel", "dab.bus_voltage", "value", "440"));

        result.Status.Should().Be("Success");
        result.Applied.Should().Be(440);
        result.Clamped.Should().BeFalse();
        engine.GetSetpoint("dab.bus_voltage").Should().Be(440);
    }

    [Fact]
    public void AValueTheProfileWillNotAdmitIsReportedAsClampedRatherThanAsSuccess()
    {
        // The honesty that matters on a commissioning run. A caller who asks for 999, gets 450 and
        // is told "Success" will believe the bus is at 999 — and that belief is the difference
        // between "the alarm did not fire" and "the alarm was never given the chance".
        ControlEndpoint.Result result = ControlEndpoint.Apply(
            Engine(), Query("cmd", "setpoint", "channel", "dab.bus_voltage", "value", "999"));

        result.Requested.Should().Be(999);
        result.Applied.Should().Be(450, "the profile declares 350..450 for this channel");
        result.Clamped.Should().BeTrue();
        result.Reason.Should().Contain("450 was applied instead");
    }

    [Theory]
    [InlineData("cmd", "setpoint", "channel", "nope", "value", "5")]
    [InlineData("cmd", "setpoint", "channel", "dab.bus_voltage", "value", "abc")]
    [InlineData("cmd", "setpoint", "value", "5")]
    [InlineData("cmd", "scenario", "id", "no-such-scenario")]
    [InlineData("cmd", "scenario")]
    [InlineData("cmd", "fly")]
    [InlineData("nothing", "at-all")]
    public void EveryRefusalNamesWhatWasWrongWithIt(params string[] pairs)
    {
        ControlEndpoint.Result result = ControlEndpoint.Apply(Engine(), Query(pairs));

        result.Status.Should().Be("Error");
        result.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AnUnknownChannelListsWhereToFindTheRealOnes()
    {
        ControlEndpoint.Result result = ControlEndpoint.Apply(
            Engine(), Query("cmd", "setpoint", "channel", "nope", "value", "5"));

        result.Reason.Should().Contain("/api/control");
        result.Channel.Should().Be("nope", "the reply echoes what was asked, not what was meant");
    }

    [Fact]
    public void AnUnknownScenarioListsTheOnesTheProfileHas()
    {
        ControlEndpoint.Result result =
            ControlEndpoint.Apply(Engine(), Query("cmd", "scenario", "id", "no-such"));

        result.Status.Should().Be("Error");
        result.Reason.Should().Contain("grid-outage");
    }

    [Fact]
    public void AScenarioMovesTheChannelsItNames()
    {
        ProfileSimulatorEngine engine = Engine();

        ControlEndpoint.Result result =
            ControlEndpoint.Apply(engine, Query("cmd", "scenario", "id", "grid-outage"));

        result.Status.Should().Be("Success");
        result.Unknown.Should().BeEmpty();
        engine.GetSetpoint("grid.voltage").Should().Be(0, "an outage takes the mains to zero");
    }

    [Fact]
    public void ResetReturnsEveryChannelToNominal()
    {
        ProfileSimulatorEngine engine = Engine();
        ControlEndpoint.Apply(engine, Query("cmd", "setpoint", "channel", "dab.bus_voltage", "value", "440"));

        ControlEndpoint.Apply(engine, Query("cmd", "reset")).Status.Should().Be("Success");

        engine.GetSetpoint("dab.bus_voltage").Should().Be(400);
    }

    // ---- injected waveforms -------------------------------------------------

    [Fact]
    public void AWaveformCanBeInjectedAndTakenAwayAgain()
    {
        // --signal could only be set at start-up, and commissioning is something someone does
        // while watching: inject an oscillation, see what the alarm does, stop it.
        ProfileSimulatorEngine engine = Engine();

        ControlEndpoint.Result on = ControlEndpoint.Apply(engine,
            Query("cmd", "signal", "channel", "dab.bus_voltage", "shape", "sine", "hz", "2", "amplitude", "20"));

        on.Status.Should().Be("Success");
        engine.InjectedSignals.Should().ContainKey("dab.bus_voltage");

        ControlEndpoint.Apply(engine, Query("cmd", "signal-off", "channel", "dab.bus_voltage"))
            .Status.Should().Be("Success");
        engine.InjectedSignals.Should().NotContainKey("dab.bus_voltage");
    }

    [Fact]
    public void ARateAboveNyquistIsRefusedRatherThanDrawnAsALowerOne()
    {
        // The one failure an injected signal must not have. Above half the sample rate the samples
        // are indistinguishable from a slower wave, so the spectrum would report a peak that is
        // real, wrong and symptomless -- on the very channel meant to be the reference.
        ProfileSimulatorEngine engine = Engine();

        ControlEndpoint.Result result = ControlEndpoint.Apply(engine,
            Query("cmd", "signal", "channel", "dab.bus_voltage", "shape", "sine", "hz", "8", "amplitude", "5"));

        result.Status.Should().Be("Error");
        result.Reason.Should().Contain("Nyquist").And.Contain("fold back");
        engine.InjectedSignals.Should().BeEmpty("nothing was applied");
    }

    [Fact]
    public void TheReplySaysThatAWideSpectrumWindowWillMixRegimes()
    {
        // Measured on a live host: the same 2 Hz signal read 1.9985 Hz over a 10 s window that was
        // entirely post-injection and 1.976 Hz over a 45 s one that reached back before it. The
        // endpoint is right both times; a caller who does not know that reads the second as a
        // wrong analyser.
        ControlEndpoint.Result result = ControlEndpoint.Apply(Engine(),
            Query("cmd", "signal", "channel", "dab.bus_voltage", "shape", "sine", "hz", "2", "amplitude", "20"));

        result.Reason.Should().Contain("reaches back before now");
    }

    [Fact]
    public void TurningOffASignalThatWasNotRunningSaysSo()
    {
        ControlEndpoint.Result result = ControlEndpoint.Apply(Engine(),
            Query("cmd", "signal-off", "channel", "dab.bus_voltage"));

        result.Status.Should().Be("Error");
        result.Reason.Should().Contain("no signal was driving");
    }

    [Fact]
    public void ADrivenChannelIsReportedAsDrivenSoAScreenCanSaySo()
    {
        // A driven channel that looks like every other one invites reading the chart as though the
        // converter were doing that.
        ProfileSimulatorEngine engine = Engine();
        ControlEndpoint.Apply(engine,
            Query("cmd", "signal", "channel", "dab.bus_voltage", "shape", "square", "hz", "1", "amplitude", "5"));

        ControlEndpoint.Result described = ControlEndpoint.Describe(engine);

        described.SampleRateHz.Should().BeGreaterThan(0, "a caller cannot judge Nyquist without it");
        described.Channels.Single(c => c.Id == "dab.bus_voltage").Signal.Should().Contain("square@1");
        described.Channels.Single(c => c.Id == "grid.voltage").Signal.Should().BeNull();
    }

    // ---- the WebSocket text path -------------------------------------------

    [Theory]
    [InlineData("setpoint dab.bus_voltage 440", "dab.bus_voltage", 440.0)]
    [InlineData("SETPOINT dab.bus_voltage 410", "dab.bus_voltage", 410.0)]
    public void ATextCommandFromTheSocketDoesWhatThePostDoes(string text, string channel, double expected)
    {
        ProfileSimulatorEngine engine = Engine();

        ControlSetup.Handle(engine, text);

        engine.GetSetpoint(channel).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("fly to the moon")]
    [InlineData("setpoint dab.bus_voltage")]
    public void ATextCommandThatMakesNoSenseChangesNothing(string text)
    {
        ProfileSimulatorEngine engine = Engine();
        double before = engine.GetSetpoint("dab.bus_voltage");

        ControlSetup.Handle(engine, text);

        engine.GetSetpoint("dab.bus_voltage").Should().Be(before);
    }

    [Fact]
    public void TheServerHandsOutNoControlForASourceThatIsNotGenerated()
    {
        // A replayed recording is not a machine to command: its values are already decided.
        var server = new TelemetryStreamingServer(port: 0);

        ControlSetup.Attach(server, source: null);

        server.Control.Should().BeNull();
        ControlEndpoint.Describe(server.Control).Status.Should().Be("Error");
    }
}
