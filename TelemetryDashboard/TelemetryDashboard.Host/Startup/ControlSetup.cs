using System;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Streaming;
using TelemetryDashboard.Host.Ingest;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Gives the running server something to command, when this run generates its own telemetry.
/// </summary>
/// <remarks>
/// The WebSocket carries text upstream and the server has raised <c>CommandReceived</c> for it
/// since M2. Nothing subscribed, so every command a console sent was raised and dropped — the
/// symptom being a control that appears to work and changes nothing, which is worse than a control
/// that is visibly absent.
/// </remarks>
public static class ControlSetup
{
    /// <summary>Attaches the source's control surface and the WebSocket command path.</summary>
    public static void Attach(TelemetryStreamingServer server, ITelemetrySource? source)
    {
        ArgumentNullException.ThrowIfNull(server);

        server.Control = source switch
        {
            SimulatedTelemetrySource simulated => simulated.Control,
            LoopbackTelemetrySource loopback => loopback.Control,
            _ => null
        };

        if (server.Control is null) return;

        server.CommandReceived += (_, text) => Handle(server.Control, text);

        Console.WriteLine("  control       setpoints and scenarios at /api/control (POST to change)");
        Console.WriteLine("                a generated source only; a real device is read-only here");
    }

    /// <summary>
    /// Runs one text command from the WebSocket, e.g. <c>setpoint grid.voltage 460</c>.
    /// </summary>
    /// <remarks>
    /// The same three commands the HTTP endpoint takes, in the shape a console already sends. The
    /// outcome is printed rather than replied: the broadcast socket has no addressed reply, and a
    /// caller that needs an answer should POST, which gives one.
    /// </remarks>
    public static void Handle(ISimulatedControl control, string text)
    {
        string[] parts = (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        var query = new System.Collections.Specialized.NameValueCollection { ["cmd"] = parts[0] };

        switch (parts[0].ToLowerInvariant())
        {
            case "setpoint" when parts.Length >= 3:
                query["channel"] = parts[1];
                query["value"] = parts[2];
                break;

            // signal <channel> <shape> <hz> [amplitude]
            case "signal" when parts.Length >= 4:
                query["channel"] = parts[1];
                query["shape"] = parts[2];
                query["hz"] = parts[3];
                if (parts.Length >= 5) query["amplitude"] = parts[4];
                break;

            case "signal-off" when parts.Length >= 2:
                query["channel"] = parts[1];
                break;

            case "scenario" when parts.Length >= 2:
                query["id"] = parts[1];
                break;

            case "reset":
                break;

            default:
                // Not a command this understands. Said, not swallowed: a console whose button does
                // nothing and reports nothing is the defect this whole path exists to remove.
                Console.Error.WriteLine($"[control] ignored '{text}': not one of setpoint, signal, signal-off, scenario, reset");
                return;
        }

        ControlEndpoint.Result result = ControlEndpoint.Apply(control, query);

        Console.WriteLine(result.Status == "Success"
            ? $"[control] {text} -> {Describe(result)}"
            : $"[control] {text} refused: {result.Reason}");
    }

    private static string Describe(ControlEndpoint.Result result) =>
        result.Applied is { } applied
            ? $"{result.Channel} = {applied:G6}" + (result.Clamped ? $" (clamped from {result.Requested:G6})" : string.Empty)
            : result.Reason ?? "applied";
}
