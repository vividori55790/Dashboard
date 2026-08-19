using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>
/// Translates a <see cref="QueryFilter"/> into the SELECT that <see cref="SqliteDataLogger"/> runs.
/// </summary>
/// <remarks>
/// The predicate is assembled from only the members the caller actually set, rather than the
/// single fixed statement of the form <c>($node IS NULL OR node_id = $node)</c>. That form reads
/// tidily but leaves SQLite unable to tell at plan time which columns are constrained, so it falls
/// back to a table scan on a log that is expected to grow to millions of rows.
/// </remarks>
internal static class SqliteTelemetryQuery
{
    /// <summary>
    /// Sets <see cref="SqliteCommand.CommandText"/> and binds every parameter for
    /// <paramref name="filter"/>. Null and empty members impose no constraint.
    /// </summary>
    /// <remarks>
    /// Text comparison is exact and case-sensitive, unlike the in-memory <c>IDataLogger</c> stand-ins
    /// elsewhere in this solution, which use <see cref="StringComparison.OrdinalIgnoreCase"/>. Node
    /// and channel identifiers arrive from firmware as exact tokens, and a NOCASE comparison would
    /// also stop the query using <c>ix_log_node_var_time</c>.
    /// <para>
    /// Ordering breaks ties on insertion order: a burst at 1 kHz easily lands many packets on the
    /// same tick, and an unstable order among them would reshuffle a plotted trace between runs.
    /// </para>
    /// </remarks>
    internal static void Configure(SqliteCommand command, QueryFilter filter)
    {
        var clauses = new List<string>(4);

        if (!string.IsNullOrEmpty(filter.NodeId))
        {
            clauses.Add("node_id = $node");
            command.Parameters.AddWithValue("$node", filter.NodeId);
        }

        if (!string.IsNullOrEmpty(filter.Variable))
        {
            clauses.Add("variable = $variable");
            command.Parameters.AddWithValue("$variable", filter.Variable);
        }

        if (filter.StartTime.HasValue)
        {
            clauses.Add("utc_ticks >= $start");
            command.Parameters.AddWithValue(
                "$start", SqliteTelemetrySchema.ToUtcTicks(filter.StartTime.Value));
        }

        if (filter.EndTime.HasValue)
        {
            clauses.Add("utc_ticks <= $end");
            command.Parameters.AddWithValue(
                "$end", SqliteTelemetrySchema.ToUtcTicks(filter.EndTime.Value));
        }

        command.Parameters.AddWithValue("$limit", filter.Limit);

        // Only compile-time literals are interpolated; every caller-supplied value is bound.
        string where = clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses);
        command.CommandText =
            $"SELECT {SqliteTelemetrySchema.Columns} FROM telemetry_log{where} " +
            "ORDER BY utc_ticks ASC, id ASC LIMIT $limit;";
    }
}
