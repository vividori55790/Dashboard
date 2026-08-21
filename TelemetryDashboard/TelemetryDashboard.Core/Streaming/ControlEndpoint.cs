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
        "scenario&id=<id>",
        "reset"
    };

    /// <summary>Reports what may be commanded and where every channel currently sits.</summary>
    public static Result Describe(ISimulatedControl? control) =>
        control is null
            ? NotControllable()
            : new Result { Command = "describe" } with
            {
                Channels = control.Profile.Channels.Select(c => new ChannelState
                {
                    Id = c.Id,
                    Label = c.Label,
                    Unit = c.Unit,
                    Minimum = c.Minimum,
                    Maximum = c.Maximum,
                    Nominal = c.Nominal,
                    Setpoint = control.GetSetpoint(c.Id)
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
