using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using TelemetryDashboard.Core.Storage;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>
/// Counts what a prune would remove, before anything is removed.
/// </summary>
/// <remarks>
/// The same predicates the delete uses, run as counts first. That is what lets a disabled policy
/// produce a truthful report of what arming it would cost, and it is also what makes the applied
/// report accurate: the numbers come from the rows themselves rather than from a delete's row
/// count, so the report can name the oldest and newest sample about to be destroyed.
/// </remarks>
internal static class TieredRetentionSurvey
{
    /// <summary>Blocks that lie entirely before the cutoff, and what they contain.</summary>
    /// <remarks>
    /// The test is <c>end_ticks &lt; cutoff</c> — the whole block must be older. A block straddling
    /// the cutoff survives intact: deleting it would take the samples on the newer side of the line
    /// with it, and rewriting it would mean decompressing, re-encoding and re-committing under the
    /// prune, which is a great deal of work to shave a partial block off the end of the window.
    /// </remarks>
    internal static (long Blocks, long Samples, long? OldestTicks, long? NewestTicks) SurveyBlocks(
        SqliteConnection connection, long cutoffTicks)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*), COALESCE(SUM(sample_count), 0), MIN(start_ticks), MAX(end_ticks)
            FROM raw_block WHERE end_ticks < $cutoff;
            """;
        command.Parameters.AddWithValue("$cutoff", cutoffTicks);

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) return (0, 0, null, null);

        return (
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.IsDBNull(2) ? null : reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3));
    }

    /// <summary>
    /// Newest bucket start a tier may delete, given its retention.
    /// </summary>
    /// <remarks>
    /// A window is only eligible once its <em>end</em> has passed out of the retention span, hence
    /// the extra bucket width. Cutting on the start would delete the window covering the last
    /// minute of the retained span while its samples were still inside it.
    /// </remarks>
    internal static long WindowCutoff(RollupInterval interval, TimeSpan retention, long nowTicks) =>
        nowTicks - retention.Ticks - interval.TicksPer();

    /// <summary>Windows each tier would lose. Tiers kept indefinitely are absent from the result.</summary>
    internal static Dictionary<RollupInterval, long> SurveyWindows(
        SqliteConnection connection, RetentionPolicy policy, long nowTicks)
    {
        var counts = new Dictionary<RollupInterval, long>();

        foreach (RollupInterval interval in RollupIntervals.All)
        {
            if (policy.RetentionFor(interval) is not { } retention) continue;

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM rollup_window WHERE interval_code = $code AND bucket_ticks <= $cutoff;";
            command.Parameters.AddWithValue("$code", (long)interval);
            command.Parameters.AddWithValue("$cutoff", WindowCutoff(interval, retention, nowTicks));

            counts[interval] = (long)(command.ExecuteScalar() ?? 0L);
        }

        return counts;
    }
}
