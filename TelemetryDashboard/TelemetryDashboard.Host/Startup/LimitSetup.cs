using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.Host.Configuration;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Resolves the engineering limits this run enforces, from the profile and the command line.
/// </summary>
/// <remarks>
/// The same two sources and the same precedence as derived channels: the profile describes the rig,
/// <c>--limit</c> describes this run, and a run may replace a rule by declaring one on the same
/// channel with the same shape.
/// <para>
/// A host with no limits gets no monitor at all rather than an empty one, so <c>/api/limits</c> can
/// say "nothing is being checked" instead of showing a clean alarm list. Those read the same and
/// mean opposite things.
/// </para>
/// </remarks>
public static class LimitSetup
{
    /// <param name="Monitor">Null when nothing was declared.</param>
    /// <param name="Warnings">Declarations that did not parse, each with the reason.</param>
    public readonly record struct Result(LimitMonitor? Monitor, IReadOnlyList<string> Warnings);

    public static Result Resolve(HostOptions options, MonitoringProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(options);

        var warnings = new List<string>();
        var byDeclaration = new List<ChannelLimit>();

        foreach (string declaration in (profile?.Limits ?? Array.Empty<string>()).Concat(options.Limits))
        {
            try
            {
                byDeclaration.Add(ChannelLimit.Parse(declaration));
            }
            catch (FormatException ex)
            {
                warnings.Add($"limit '{declaration}' was skipped: {ex.Message}");
            }
        }

        // Later wins on an identical declaration, so repeating one on the command line is not two
        // rules watching the same band and reporting every excursion twice.
        List<ChannelLimit> rules = byDeclaration
            .GroupBy(r => r.Declaration, StringComparer.Ordinal)
            .Select(g => g.Last())
            .ToList();

        return new Result(rules.Count == 0 ? null : new LimitMonitor(rules), warnings);
    }

    /// <summary>Attaches the monitor to the running server and prints what is in force.</summary>
    public static void Attach(
        HostOptions options, Core.Streaming.TelemetryStreamingServer server, MonitoringProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(server);

        Result resolved = Resolve(options, profile);
        server.Limits = resolved.Monitor;

        foreach (string warning in resolved.Warnings) Console.WriteLine($"  limits        ! {warning}");

        if (resolved.Monitor is not { } monitor) return;

        Console.WriteLine($"  limits        {monitor.Rules.Count} engineering limit(s) at /api/limits");
        foreach (ChannelLimit rule in monitor.Rules) Console.WriteLine($"                {rule.Declaration}");
    }
}
