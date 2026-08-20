using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TelemetryDashboard.Core.Storage;

/// <summary>
/// What a prune removed, or would have removed. Returned by every prune, armed or not.
/// </summary>
/// <remarks>
/// Destroying data silently is the thing this record exists to prevent. It names the cutoff, the
/// oldest and newest sample inside the deleted range and the exact counts, so the operator's answer
/// to "what did we lose?" comes from the store rather than from a guess. <see cref="Applied"/>
/// distinguishes a dry run from a deletion — the two are otherwise identical in shape, which is
/// what makes a dry run a useful rehearsal.
/// </remarks>
public sealed record RetentionPruneReport(
    DateTime ExecutedUtc,
    bool Applied,
    DateTime RawCutoffUtc,
    long RawBlocksRemoved,
    long RawSamplesRemoved,
    IReadOnlyDictionary<RollupInterval, long> RollupWindowsRemoved,
    DateTime? OldestRemovedUtc,
    DateTime? NewestRemovedUtc)
{
    /// <summary>Total rollup windows removed across every tier.</summary>
    public long TotalRollupWindowsRemoved => RollupWindowsRemoved.Values.Sum();

    /// <summary>Whether anything at all was, or would have been, removed.</summary>
    public bool RemovedAnything => RawBlocksRemoved > 0 || TotalRollupWindowsRemoved > 0;

    /// <summary>A single log line stating exactly what happened.</summary>
    public string Describe()
    {
        string verb = Applied ? "removed" : "would remove (dry run, retention disabled)";
        string tiers = RollupWindowsRemoved.Count == 0
            ? "no rollup tiers"
            : string.Join(", ", RollupIntervals.All
                .Where(RollupWindowsRemoved.ContainsKey)
                .Select(i => $"{RollupWindowsRemoved[i]} {i.ToString().ToLowerInvariant()} windows"));

        string range = OldestRemovedUtc is null || NewestRemovedUtc is null
            ? "no samples in range"
            : $"{Stamp(OldestRemovedUtc.Value)} .. {Stamp(NewestRemovedUtc.Value)}";

        return $"retention {verb}: {RawBlocksRemoved} raw blocks / {RawSamplesRemoved} samples " +
               $"({range}), {tiers}; raw cutoff {Stamp(RawCutoffUtc)}, run at {Stamp(ExecutedUtc)}";
    }

    private static string Stamp(DateTime value) =>
        value.ToString("yyyy-MM-dd HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
