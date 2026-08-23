using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TelemetryDashboard.Core.Ingest;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.Host.Configuration;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Deciding what this run believes the device on the wire is sending.
/// </summary>
/// <remarks>
/// The only rules any front end registered were <see cref="DefaultRoutingRules"/>, which recognise
/// the framing this product's own generated firmware emits. That is precisely the firmware a real
/// installation does not have, so a bench MCU naming its own channels arrived, charted itself, and
/// matched no band, no computed channel and no twin placement the profile declared.
/// <para>
/// A file replaces the defaults rather than adding to them. The router keeps its rules in a
/// dictionary and iterates it in whatever order it likes, so two rules matching one frame is not
/// two configurations but an ambiguity that could resolve differently between two runs of the same
/// build. A file describing this device IS the routing configuration, and the sample beside it
/// starts from a $TELE rule, so the ordinary case loses nothing by replacing.
/// </para>
/// </remarks>
public static class RoutingSetup
{
    /// <param name="Rules">What to register, defaults included when no file was given.</param>
    /// <param name="Lines">What to print, already indented for the banner.</param>
    public readonly record struct Result(IReadOnlyList<RoutingRule> Rules, IReadOnlyList<string> Lines);

    /// <summary>Loads and audits the rules, or says why the file cannot be used.</summary>
    /// <returns>False only when a file was named and cannot be honoured.</returns>
    public static bool TryResolve(
        HostOptions options, MonitoringProfile? profile, out Result result, out string? refusal)
    {
        refusal = null;
        try
        {
            result = Resolve(options, profile);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            // Refused rather than started without them. A hub that ignored an unreadable rule file
            // would run with the built-in framing and report nothing unusual, which is the same
            // shape as the defect this whole file exists to remove.
            result = default;
            refusal = ex.Message;
            return false;
        }
    }

    /// <summary>Loads and audits the rules, or explains why the file cannot be used.</summary>
    /// <exception cref="InvalidDataException">The file exists and is not usable.</exception>
    public static Result Resolve(HostOptions options, MonitoringProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.RulesPath is not { } path)
        {
            return new Result(DefaultRoutingRules.Create(), []);
        }

        RoutingRuleReader.Result read = RoutingRuleReader.Load(path);
        var lines = new List<string>
        {
            $"  wire rules    {read.Rules.Count} from {Path.GetFileName(path)} "
            + $"(replacing the {DefaultRoutingRules.Create().Count} built-in)"
        };

        foreach (string warning in read.Warnings) lines.Add($"                ! {warning}");

        int mapped = read.Rules.Sum(r => r.NameMap.Count);
        if (mapped > 0) lines.Add($"                {mapped} device name(s) mapped onto declared channels");

        // Said here, where somebody is still looking. Every one of these is a mapping that will be
        // accepted, deliver data, and be judged by nothing.
        foreach (string finding in RoutingRuleAudit.Check(read.Rules, profile))
        {
            lines.Add($"                ! {finding}");
        }

        IReadOnlyList<string> silent = RoutingRuleAudit.Unmapped(read.Rules, profile);
        if (silent.Count > 0 && profile is not null)
        {
            // Not a fault: a rig is commissioned in stages, and a device already speaking the
            // declared ids needs no mapping. It is said because "my channel is missing" is the
            // question this answers before it is asked.
            lines.Add($"                {silent.Count} declared channel(s) have no mapping: "
                + string.Join(", ", silent.Take(6))
                + (silent.Count > 6 ? $", and {silent.Count - 6} more" : string.Empty));
            lines.Add("                Those arrive only if the device already calls them that.");
        }

        return new Result(read.Rules, lines);
    }
}
