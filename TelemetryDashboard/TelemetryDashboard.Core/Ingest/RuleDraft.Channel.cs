using System;
using System.Collections.Generic;
using System.Text;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Core.Ingest;

public static partial class RuleDraft
{
    private static void Channel(
        StringBuilder draft, WireChannel channel,
        Dictionary<string, string> mapped, MonitoringProfile? profile)
    {
        string unit = channel.Unit.Length > 0 ? channel.Unit : "no unit";
        draft.AppendLine(
            $"        // {channel.Name}: {unit}, {channel.Range}, {channel.Samples:N0} sample(s).");

        if (mapped.TryGetValue(channel.Name, out string? declared))
        {
            ProfileChannel? target = Declared(profile, declared);
            double? gain = UnitScale.Between(channel.Unit, target?.Unit);

            draft.AppendLine($"        //   the profile declares this name, so it is filled in.");
            if (gain is not null && gain != 1.0)
            {
                draft.AppendLine(
                    $"        //   {channel.Unit} to {target!.Unit} is a unit conversion, not a guess.");
            }
            else if (target is not null && !string.Equals(channel.Unit, target.Unit, StringComparison.Ordinal))
            {
                draft.AppendLine(
                    $"        //   the device says {unit} and the profile declares {Show(target.Unit)}; "
                    + "these are not the same quantity, so no scale is written for you.");
            }

            draft.AppendLine($"        \"{channel.Name}\": {Alias(declared, target?.Unit, gain)},");
            return;
        }

        // Pre-filled with the best fit and left commented out. The smallest edit that could
        // accept it is deleting two characters, and until somebody does, it maps nothing.
        IReadOnlyList<ChannelCandidate> fits = Candidates(channel, profile, mapped);
        foreach (string line in CandidateLines(fits, profile is not null)) draft.AppendLine(line);

        string alias = IsDecisive(fits)
            ? Alias(fits[0].Declared.Id, fits[0].Declared.Unit, fits[0].Gain)
            : "{ \"channel\": \"\" }";

        draft.AppendLine($"        // \"{channel.Name}\": {alias},");
    }

    /// <summary>
    /// One alias entry. The unit travels with the gain, never on its own.
    /// </summary>
    /// <remarks>
    /// Relabelling millivolts as volts without dividing by a thousand is worse than leaving the
    /// unit alone: the band would then judge, and judge a number a thousand times too large.
    /// </remarks>
    private static string Alias(string channel, string? unit, double? gain)
    {
        var parts = new List<string> { $"\"channel\": \"{channel}\"" };

        if (gain is not null && gain != 1.0)
        {
            if (!string.IsNullOrWhiteSpace(unit)) parts.Add($"\"unit\": \"{unit}\"");
            parts.Add($"\"gain\": {UnitScale.Format(gain.Value)}");
        }

        return "{ " + string.Join(", ", parts) + " }";
    }
}
