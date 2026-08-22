using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// Answers <c>/api/control</c>: the one place the cross-platform product is not read-only.
/// </summary>
/// <remarks>
/// A browser could watch, query and be alerted, and could change nothing. The streaming server even
/// raised a <c>CommandReceived</c> event for text arriving on the WebSocket, and nothing anywhere
/// subscribed to it — so a command sent from a console was raised and dropped.
/// <para>
/// What that costs is commissioning. An engineer installing this has to prove the alarm fires and
/// the interlock trips before trusting either, and with no way to put a channel at a chosen value
/// the only proof available is over-volting real hardware.
/// </para>
/// <para>
/// Offered only for a generated source. A host reading a real device has no control object at all,
/// and the endpoint says so rather than accepting a command it will not carry out: moving that
/// machine is a command to the machine, which is the emergency interlock's job and is armed
/// separately, deliberately, and never from a browser.
/// </para>
/// </remarks>
public static partial class ControlEndpoint
{
    /// <summary>Commands this endpoint understands, listed in its own reply.</summary>
    public static readonly IReadOnlyList<string> Commands = new[]
    {
        "setpoint&channel=<id>&value=<number>",
        "signal&channel=<id>&shape=<sine|square|triangle|sawtooth|noise>&hz=<number>&amplitude=<number>",
        "signal-off&channel=<id>",
        "scenario&id=<id>",
        "reset"
    };

    /// <summary>Reports what may be commanded and where every channel currently sits.</summary>
    public static Result Describe(ISimulatedControl? control) =>
        control is null
            ? NotControllable()
            : new Result { Command = "describe" } with
            {
                SampleRateHz = control.SampleRateHz,
                Channels = control.Profile.Channels.Select(c => new ChannelState
                {
                    Id = c.Id,
                    Label = c.Label,
                    Unit = c.Unit,
                    Minimum = c.Minimum,
                    Maximum = c.Maximum,
                    Nominal = c.Nominal,
                    Setpoint = control.GetSetpoint(c.Id),
                    Signal = control.InjectedSignals.TryGetValue(c.Id, out Analytics.InjectedSignal? driving)
                        ? driving.Declaration
                        : null
                }).ToList(),
                Scenarios = control.Profile.Scenarios.Select(s => new ScenarioState
                {
                    Id = s.Id,
                    Label = s.Label,
                    Description = s.Description
                }).ToList()
            };

    /// <summary>Applies one command.</summary>
    public static Result Apply(ISimulatedControl? control, NameValueCollection query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (control is null) return NotControllable();

        string command = (query["cmd"] ?? string.Empty).Trim().ToLowerInvariant();

        return command switch
        {
            "setpoint" => Setpoint(control, query),
            "signal" => Signal(control, query),
            "signal-off" => SignalOff(control, query["channel"]),
            "scenario" => Scenario(control, query["id"]),
            "reset" => Reset(control),
            "" => Refuse(null, "no command given; pass ?cmd=" + string.Join(" or ?cmd=", Commands)),
            _ => Refuse(command, $"'{command}' is not a command here; try {string.Join(", ", Commands)}")
        };
    }

    private static Result Setpoint(ISimulatedControl control, NameValueCollection query)
    {
        string channel = (query["channel"] ?? string.Empty).Trim();
        if (channel.Length == 0) return Refuse("setpoint", "no channel named; pass &channel=<id>");

        if (!double.TryParse(query["value"], NumberStyles.Float, CultureInfo.InvariantCulture, out double requested)
            || !double.IsFinite(requested))
        {
            return Refuse("setpoint", $"'{query["value"]}' is not a finite number");
        }

        double applied = control.SetSetpoint(channel, requested);

        if (double.IsNaN(applied))
        {
            return Refuse("setpoint",
                $"this profile declares no channel '{channel}'; GET /api/control lists the ones it does")
                with { Channel = channel, Requested = requested };
        }

        return new Result
        {
            Command = "setpoint",
            Channel = channel,
            Requested = requested,
            Applied = applied,
            Clamped = Math.Abs(applied - requested) > 1e-9,
            Reason = Math.Abs(applied - requested) > 1e-9
                ? string.Create(CultureInfo.InvariantCulture,
                    $"{requested:G6} is outside the range this profile declares for '{channel}'; " +
                    $"{applied:G6} was applied instead")
                : null
        };
    }

    private static Result Signal(ISimulatedControl control, NameValueCollection query)
    {
        string channel = (query["channel"] ?? string.Empty).Trim();
        if (channel.Length == 0) return Refuse("signal", "no channel named; pass &channel=<id>");

        string shape = (query["shape"] ?? "sine").Trim();
        string hz = (query["hz"] ?? string.Empty).Trim();
        string amplitude = (query["amplitude"] ?? "1").Trim();

        Analytics.InjectedSignal signal;
        try
        {
            signal = Analytics.InjectedSignal.Parse($"{channel}={shape}@{hz}:{amplitude}");
        }
        catch (FormatException ex)
        {
            return Refuse("signal", ex.Message) with { Channel = channel };
        }

        // Refused here rather than accepted and drawn. Above half the sample rate the samples are
        // indistinguishable from a slower wave, so the spectrum would report a peak that is real,
        // wrong and has no symptom -- and an injected signal exists to be the thing other
        // measurements are checked against.
        if (signal.AliasesAt(control.SampleRateHz))
        {
            return Refuse("signal",
                $"{signal.FrequencyHz:G6} Hz is above the {control.SampleRateHz / 2:G6} Hz Nyquist "
                + $"limit of this source ({control.SampleRateHz:G6} Hz per channel). It would fold "
                + "back and be reported as a lower frequency that is not there.")
                with { Channel = channel };
        }

        if (!control.InjectSignal(signal))
        {
            return Refuse("signal",
                $"this profile declares no channel '{channel}'; GET /api/control lists the ones it does")
                with { Channel = channel };
        }

        return new Result
        {
            Command = "signal",
            Channel = channel,
            Reason = $"{signal.Shape.ToString().ToLowerInvariant()} at {signal.FrequencyHz:G6} Hz, "
                   + $"±{signal.Amplitude:G6} about the setpoint. This channel is now a reference, "
                   + "not a simulation of the machine. A spectrum taken over a window that reaches "
                   + "back before now mixes this with what the channel was doing before, and the "
                   + "step between them dominates the low end: measured at 1.976 Hz over 45 s "
                   + "against 1.9985 Hz over 10 s, for the same 2 Hz signal."
        };
    }

    private static Result SignalOff(ISimulatedControl control, string? channelId)
    {
        string channel = (channelId ?? string.Empty).Trim();
        if (channel.Length == 0) return Refuse("signal-off", "no channel named; pass &channel=<id>");

        return control.ClearSignal(channel)
            ? new Result { Command = "signal-off", Channel = channel }
            : Refuse("signal-off", $"no signal was driving '{channel}'") with { Channel = channel };
    }

    private static Result Scenario(ISimulatedControl control, string? id)
    {
        string scenario = (id ?? string.Empty).Trim();
        if (scenario.Length == 0) return Refuse("scenario", "no scenario named; pass &id=<id>");

        IReadOnlyList<string> unknown = control.ApplyScenario(scenario);

        // The engine returns the scenario id itself when there is no such scenario, and the channel
        // ids it could not find otherwise. Those are different failures and read differently.
        if (unknown.Count == 1 && unknown[0] == scenario
            && !control.Profile.Scenarios.Any(s => s.Id == scenario))
        {
            return Refuse("scenario",
                $"this profile declares no scenario '{scenario}'; it has: " +
                string.Join(", ", control.Profile.Scenarios.Select(s => s.Id)));
        }

        return new Result
        {
            Command = "scenario",
            Channel = scenario,
            Unknown = unknown,
            Reason = unknown.Count == 0
                ? null
                : "applied, but this scenario names channels the profile does not declare: "
                  + string.Join(", ", unknown)
        };
    }

    private static Result Reset(ISimulatedControl control)
    {
        control.Reset();
        return new Result { Command = "reset" };
    }

    private static Result NotControllable() => new()
    {
        Status = "Error",
        Reason = "this host has nothing it may command. Setpoints belong to a generated source — "
               + "start it with --simulate or --serial loopback. A host reading a real device is "
               + "deliberately read-only here; acting on that machine is the emergency interlock's "
               + "job and is armed separately."
    };

    private static Result Refuse(string? command, string reason) =>
        new() { Status = "Error", Command = command, Reason = reason };
}
