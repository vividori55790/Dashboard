using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.Host.Configuration;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Resolves the derived channels this run serves, from the profile and the command line.
/// </summary>
/// <remarks>
/// Two sources, one list, and the precedence stated rather than emergent: the profile describes the
/// rig, <c>--computed</c> describes this run, and a run may override the rig by reusing an id. The
/// alternative — command-line declarations simply appended — would leave two channels answering to
/// one id and no rule about which one a client got.
/// </remarks>
public static class ComputedChannelSetup
{
    /// <summary>What resolution produced: the channels to serve, and anything worth saying about them.</summary>
    /// <param name="Channels">Declarations that parsed, profile first, overrides applied.</param>
    /// <param name="Warnings">Declarations that did not parse, each with the reason.</param>
    public readonly record struct Result(IReadOnlyList<ComputedChannel> Channels, IReadOnlyList<string> Warnings);

    /// <summary>Builds the list, skipping and reporting anything that does not parse.</summary>
    /// <remarks>
    /// A profile declaration that fails here has already been reported by the profile reader, which
    /// checks both that it parses and that it reads channels the profile declares. It is re-parsed
    /// rather than trusted because the reader hands back text, and re-deriving the object from the
    /// text is what keeps the two from drifting.
    /// </remarks>
    public static Result Resolve(HostOptions options, MonitoringProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(options);

        var warnings = new List<string>();
        var byId = new Dictionary<string, ComputedChannel>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (string declaration in (profile?.Computed ?? Array.Empty<string>()).Concat(options.Computed))
        {
            ComputedChannel parsed;
            try
            {
                parsed = ComputedChannel.Parse(declaration);
            }
            catch (FormatException ex)
            {
                warnings.Add($"computed channel '{declaration}' was skipped: {ex.Message}");
                continue;
            }

            if (!byId.ContainsKey(parsed.Id)) order.Add(parsed.Id);
            byId[parsed.Id] = parsed;
        }

        return new Result(order.Select(id => byId[id]).ToList(), warnings);
    }

    /// <summary>Resolves the derived channels and hands them to the running server.</summary>
    /// <remarks>
    /// The profile is passed in rather than resolved here, so every feature of a run is configured
    /// from one reading of it.
    /// </remarks>
    public static void Attach(
        HostOptions options, Core.Streaming.TelemetryStreamingServer server, MonitoringProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(server);

        Result resolved = Resolve(options, profile);
        server.Computed = resolved.Channels;

        foreach (string line in BannerLines(resolved)) Console.WriteLine(line);
    }

    /// <summary>Banner lines describing what will be served, or nothing when none was declared.</summary>
    public static IEnumerable<string> BannerLines(Result resolved)
    {
        foreach (string warning in resolved.Warnings)
        {
            yield return $"  computed      ! {warning}";
        }

        if (resolved.Channels.Count == 0) yield break;

        yield return $"  computed      {resolved.Channels.Count} derived channel(s) at /api/computed";

        foreach (ComputedChannel channel in resolved.Channels)
        {
            string unit = string.IsNullOrEmpty(channel.Unit) ? string.Empty : $" [{channel.Unit}]";
            yield return $"                {channel.Id}{unit} = {channel.Expression}";
        }
    }
}
