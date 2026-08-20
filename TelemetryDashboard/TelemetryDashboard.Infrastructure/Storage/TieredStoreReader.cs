using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using TelemetryDashboard.Core.Storage;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>Range reads over the tiered store's two data tables.</summary>
/// <remarks>
/// Predicates are assembled from the constraints a caller actually set, for the reason given in
/// <see cref="SqliteTelemetryQuery"/>: a fixed <c>($x IS NULL OR col = $x)</c> shape leaves SQLite
/// unable to see at plan time which columns are constrained, and it falls back to a scan.
/// </remarks>
internal static class TieredStoreReader
{
    /// <summary>Blocks holding at least one sample inside the tick range, oldest first.</summary>
    /// <remarks>
    /// The overlap test is on the block's own range, so a block is skipped without being
    /// decompressed. Blocks are returned whole; trimming to the exact window happens after the
    /// decode, where the individual timestamps are known.
    /// </remarks>
    internal static List<CompressedSampleBlock> ReadBlocks(
        SqliteConnection connection, string? nodeId, string? variable, long startTicks, long endTicks)
    {
        using SqliteCommand command = connection.CreateCommand();
        var clauses = new List<string> { "end_ticks >= $start", "start_ticks <= $end" };
        command.Parameters.AddWithValue("$start", startTicks);
        command.Parameters.AddWithValue("$end", endTicks);

        if (!string.IsNullOrEmpty(nodeId))
        {
            clauses.Insert(0, "node_id = $node");
            command.Parameters.AddWithValue("$node", nodeId);
        }

        if (!string.IsNullOrEmpty(variable))
        {
            clauses.Insert(1, "variable = $variable");
            command.Parameters.AddWithValue("$variable", variable);
        }

        command.CommandText =
            $"SELECT {TieredStoreSchema.BlockColumns} FROM raw_block WHERE {string.Join(" AND ", clauses)} " +
            "ORDER BY start_ticks ASC, id ASC;";

        var blocks = new List<CompressedSampleBlock>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) blocks.Add(TieredStoreSchema.ReadBlock(reader));
        return blocks;
    }

    /// <summary>
    /// Stored windows of one tier for one channel, oldest first.
    /// </summary>
    /// <remarks>
    /// The range starts at the aligned bucket containing <paramref name="startTicks"/>, so a window
    /// straddling the start of the query is returned rather than dropped. It carries its own start
    /// and end, so a caller can see that it reaches back before what was asked for.
    /// <para>
    /// Buckets with no measurement are not absent by filtering here — they were never written. An
    /// interval in which every reading was NaN, or in which nothing arrived at all, has no row, so
    /// a gap arrives as a gap.
    /// </para>
    /// </remarks>
    internal static List<RollupWindow> ReadWindows(
        SqliteConnection connection,
        ChannelKey channel,
        RollupInterval interval,
        long startTicks,
        long endTicks,
        int limit)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {TieredStoreSchema.WindowColumns} FROM rollup_window " +
            "WHERE node_id = $node AND variable = $variable AND interval_code = $code " +
            "AND bucket_ticks >= $start AND bucket_ticks <= $end " +
            "ORDER BY bucket_ticks ASC LIMIT $limit;";
        command.Parameters.AddWithValue("$node", channel.NodeId);
        command.Parameters.AddWithValue("$variable", channel.Variable);
        command.Parameters.AddWithValue("$code", (long)interval);
        command.Parameters.AddWithValue("$start", interval.BucketStartTicks(startTicks));
        command.Parameters.AddWithValue("$end", endTicks);
        command.Parameters.AddWithValue("$limit", limit);

        var windows = new List<RollupWindow>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) windows.Add(TieredStoreSchema.ReadWindow(reader, interval));
        return windows;
    }

    /// <summary>
    /// Timestamp of the oldest raw sample still held for a channel, or null when none is.
    /// </summary>
    /// <remarks>
    /// This is what tells the tier selector that raw data has been pruned out from under a query.
    /// Without it a window older than retention would come back empty and read as "the sensor was
    /// silent" rather than "those samples are gone".
    /// </remarks>
    internal static long? EarliestBlockTicks(SqliteConnection connection, ChannelKey channel)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT MIN(start_ticks) FROM raw_block WHERE node_id = $node AND variable = $variable;";
        command.Parameters.AddWithValue("$node", channel.NodeId);
        command.Parameters.AddWithValue("$variable", channel.Variable);

        object? value = command.ExecuteScalar();
        return value is long ticks ? ticks : null;
    }
}
