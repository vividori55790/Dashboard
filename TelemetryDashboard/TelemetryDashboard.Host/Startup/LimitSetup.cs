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

    /// <summary>Combines the profile's declared bands with any given on the command line.</summary>
    /// <remarks>
    /// The parsing itself is <see cref="Core.Analytics.LimitDeclarations"/>, in Core, because the
    /// desktop shell needs the same answer from the same text — and had no way to reach this.
    /// </remarks>
    public static Result Resolve(HostOptions options, MonitoringProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(options);

        Core.Analytics.LimitDeclarations.Resolution resolved = Core.Analytics.LimitDeclarations.Resolve(
            (profile?.Limits ?? Array.Empty<string>()).Concat(options.Limits));

        return new Result(resolved.Monitor, resolved.Warnings);
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
