using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using TelemetryDashboard.Core.Storage;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>
/// Applies a retention policy, and records what it did in the database itself.
/// </summary>
/// <remarks>
/// Two rules govern this type. Nothing is deleted unless <see cref="RetentionPolicy.Enabled"/> is
/// set, so a store that was never configured for retention keeps everything however old it gets.
/// And every run — armed or not — appends a row to <c>retention_log</c> inside the same transaction
/// as the deletes, so a database can always answer what was removed from it and when. A deletion
/// that leaves no trace is indistinguishable afterwards from data that never arrived.
/// </remarks>
internal static class TieredRetentionPruner
{
    /// <summary>Surveys, deletes if armed, logs either way, and returns the report.</summary>
    internal static RetentionPruneReport Prune(
        SqliteConnection connection, RetentionPolicy policy, DateTime nowUtc)
    {
        policy.Validated();

        long nowTicks = RollupIntervals.ToUtcTicks(nowUtc);
        long rawCutoff = nowTicks - policy.RawRetention.Ticks;

        (long blocks, long samples, long? oldest, long? newest) =
            TieredRetentionSurvey.SurveyBlocks(connection, rawCutoff);
        Dictionary<RollupInterval, long> windows =
            TieredRetentionSurvey.SurveyWindows(connection, policy, nowTicks);

        var report = new RetentionPruneReport(
            new DateTime(nowTicks, DateTimeKind.Utc),
            policy.Enabled,
            new DateTime(rawCutoff, DateTimeKind.Utc),
            blocks,
            samples,
            windows,
            oldest is { } o ? new DateTime(o, DateTimeKind.Utc) : null,
            newest is { } n ? new DateTime(n, DateTimeKind.Utc) : null);

        using SqliteTransaction transaction = connection.BeginTransaction();
        if (policy.Enabled)
        {
            DeleteBlocks(connection, transaction, rawCutoff);
            DeleteWindows(connection, transaction, policy, nowTicks);
        }

        Log(connection, transaction, nowTicks, report);
        transaction.Commit();
        return report;
    }

    private static void DeleteBlocks(
        SqliteConnection connection, SqliteTransaction transaction, long cutoffTicks)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM raw_block WHERE end_ticks < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", cutoffTicks);
        command.ExecuteNonQuery();
    }

    private static void DeleteWindows(
        SqliteConnection connection, SqliteTransaction transaction, RetentionPolicy policy, long nowTicks)
    {
        foreach (RollupInterval interval in RollupIntervals.All)
        {
            if (policy.RetentionFor(interval) is not { } retention) continue;

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "DELETE FROM rollup_window WHERE interval_code = $code AND bucket_ticks <= $cutoff;";
            command.Parameters.AddWithValue("$code", (long)interval);
            command.Parameters.AddWithValue(
                "$cutoff", TieredRetentionSurvey.WindowCutoff(interval, retention, nowTicks));
            command.ExecuteNonQuery();
        }
    }

    private static void Log(
        SqliteConnection connection, SqliteTransaction transaction, long nowTicks, RetentionPruneReport report)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO retention_log (executed_ticks, applied, detail) VALUES ($ticks, $applied, $detail);";
        command.Parameters.AddWithValue("$ticks", nowTicks);
        command.Parameters.AddWithValue("$applied", report.Applied ? 1L : 0L);
        command.Parameters.AddWithValue("$detail", report.Describe());
        command.ExecuteNonQuery();
    }
}
