using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Query;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// Maps the channel names an expression uses onto the keys the series store actually holds.
/// </summary>
/// <remarks>
/// A profile calls a channel <c>dab.bus_voltage</c>. The store keys it <c>SIM:COM3.dab.bus_voltage</c>,
/// because the same quantity from two converters has to stay two series. Both names are right and
/// neither can be changed to match the other: a profile describes the rig and cannot know which
/// node will report it — or whether the run will be simulated, which is where the <c>SIM:</c> comes
/// from — while the store cannot merge nodes without losing the ability to tell them apart.
/// <para>
/// So an unqualified name is resolved against what has actually arrived, and the one case that
/// invites a wrong answer is refused: when two nodes both report the channel, there is no basis for
/// choosing one, and choosing anyway would compute a converter's efficiency from another
/// converter's current. The caller is told to qualify it as <c>[node].channel</c>, which the
/// expression syntax already supports.
/// </para>
/// </remarks>
public static class ComputedInputResolver
{
    /// <summary>What one input name resolved to.</summary>
    /// <param name="Key">The series key to read, or null when it could not be resolved.</param>
    /// <param name="Reason">Why not. Null when <paramref name="Key"/> is set.</param>
    public readonly record struct Resolution(string? Key, string? Reason);

    /// <summary>Resolves <paramref name="input"/> against the channels the store holds.</summary>
    public static Resolution Resolve(SeriesStore store, string input)
    {
        ArgumentNullException.ThrowIfNull(store);

        // An exact key wins outright. A fully qualified name is the caller being specific, and
        // second-guessing it would make qualification useless for the case it exists to settle.
        if (store.Find(input) is not null) return new Resolution(input, null);

        string suffix = "." + input;
        string[] candidates = store.Channels
            .Where(c => c.EndsWith(suffix, StringComparison.Ordinal))
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToArray();

        return candidates.Length switch
        {
            1 => new Resolution(candidates[0], null),
            0 => new Resolution(null, $"'{input}' has reported nothing on this host"),
            _ => new Resolution(
                null,
                $"'{input}' is reported by {candidates.Length} nodes ({string.Join(", ", candidates)}), " +
                $"so it names no single series here; qualify it as [node].{input}")
        };
    }
}
