using System.Collections.Generic;
using System.Threading;
using Microsoft.Data.Sqlite;
using TelemetryDashboard.Core.Storage;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>
/// Commits one batch: its compressed blocks and its rollup contributions, in a single transaction.
/// </summary>
/// <remarks>
/// Both halves land together or neither does. Splitting them would let a crash leave rollups that
/// summarise samples the store does not have, or samples that no summary counts — and the second
/// is worse, because the coarse tiers would go on answering queries with a number that is quietly
/// short. Cancellation mid-batch leaves the transaction uncommitted, so it rolls back on dispose.
/// </remarks>
internal static class TieredStoreWriter
{
    private const string InsertBlockSql =
        """
        INSERT INTO raw_block (node_id, variable, unit, start_ticks, end_ticks, sample_count, codec, payload)
        VALUES ($node, $variable, $unit, $start, $end, $count, $codec, $payload);
        """;

    /// <summary>
    /// Merges a batch's partial window into whatever is already stored for that bucket.
    /// </summary>
    /// <remarks>
    /// This is what makes the rollups incremental. The stored row is the running aggregate, and a
    /// batch contributes to it without anything ever re-reading a raw sample: counts and sums add,
    /// the extremes take the wider of the two, and <c>m2</c> combines by Chan's parallel formula so
    /// the standard deviation is the one a single pass over all the samples would have produced.
    /// <para>
    /// Every right-hand side sees the pre-update row, which is why <c>m2</c> can be written first
    /// and still use the old count and sum. <c>m2</c>'s leading term is a REAL product, so the
    /// count ratio that follows is evaluated in floating point rather than truncated by integer
    /// division.
    /// </para>
    /// </remarks>
    private const string MergeWindowSql =
        """
        INSERT INTO rollup_window
            (node_id, variable, interval_code, bucket_ticks, sample_count, min_value, max_value, sum_value, m2)
        VALUES ($node, $variable, $code, $bucket, $count, $min, $max, $sum, $m2)
        ON CONFLICT (node_id, variable, interval_code, bucket_ticks) DO UPDATE SET
            m2 = rollup_window.m2 + excluded.m2
               + (excluded.sum_value / excluded.sample_count - rollup_window.sum_value / rollup_window.sample_count)
               * (excluded.sum_value / excluded.sample_count - rollup_window.sum_value / rollup_window.sample_count)
               * rollup_window.sample_count * excluded.sample_count
               / (rollup_window.sample_count + excluded.sample_count),
            sample_count = rollup_window.sample_count + excluded.sample_count,
            min_value    = MIN(rollup_window.min_value, excluded.min_value),
            max_value    = MAX(rollup_window.max_value, excluded.max_value),
            sum_value    = rollup_window.sum_value + excluded.sum_value;
        """;

    /// <summary>Writes every block and merges every window, then commits.</summary>
    /// <exception cref="OperationCanceledException">Cancelled before the commit.</exception>
    internal static void Commit(
        SqliteConnection connection,
        IReadOnlyList<CompressedSampleBlock> blocks,
        IReadOnlyList<RollupWindow> windows,
        CancellationToken cancellationToken)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        WriteBlocks(connection, transaction, blocks, cancellationToken);
        MergeWindows(connection, transaction, windows, cancellationToken);
        transaction.Commit();
    }

    private static void WriteBlocks(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<CompressedSampleBlock> blocks,
        CancellationToken cancellationToken)
    {
        if (blocks.Count == 0) return;

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = InsertBlockSql;

        SqliteParameter node = command.Parameters.Add("$node", SqliteType.Text);
        SqliteParameter variable = command.Parameters.Add("$variable", SqliteType.Text);
        SqliteParameter unit = command.Parameters.Add("$unit", SqliteType.Text);
        SqliteParameter start = command.Parameters.Add("$start", SqliteType.Integer);
        SqliteParameter end = command.Parameters.Add("$end", SqliteType.Integer);
        SqliteParameter count = command.Parameters.Add("$count", SqliteType.Integer);
        SqliteParameter payload = command.Parameters.Add("$payload", SqliteType.Blob);
        command.Parameters.AddWithValue("$codec", GorillaBlockCodec.CodecId);
        command.Prepare();

        foreach (CompressedSampleBlock block in blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            node.Value = block.Channel.NodeId;
            variable.Value = block.Channel.Variable;
            unit.Value = block.Unit;
            start.Value = block.StartUtcTicks;
            end.Value = block.EndUtcTicks;
            count.Value = block.SampleCount;
            payload.Value = block.Payload;
            command.ExecuteNonQuery();
        }
    }

    private static void MergeWindows(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<RollupWindow> windows,
        CancellationToken cancellationToken)
    {
        if (windows.Count == 0) return;

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = MergeWindowSql;

        SqliteParameter node = command.Parameters.Add("$node", SqliteType.Text);
        SqliteParameter variable = command.Parameters.Add("$variable", SqliteType.Text);
        SqliteParameter code = command.Parameters.Add("$code", SqliteType.Integer);
        SqliteParameter bucket = command.Parameters.Add("$bucket", SqliteType.Integer);
        SqliteParameter count = command.Parameters.Add("$count", SqliteType.Integer);
        SqliteParameter min = command.Parameters.Add("$min", SqliteType.Real);
        SqliteParameter max = command.Parameters.Add("$max", SqliteType.Real);
        SqliteParameter sum = command.Parameters.Add("$sum", SqliteType.Real);
        SqliteParameter m2 = command.Parameters.Add("$m2", SqliteType.Real);
        command.Prepare();

        foreach (RollupWindow window in windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            node.Value = window.Channel.NodeId;
            variable.Value = window.Channel.Variable;
            code.Value = (long)window.Interval;
            bucket.Value = window.BucketStartUtcTicks;
            count.Value = window.Count;
            min.Value = window.Min;
            max.Value = window.Max;
            sum.Value = window.Sum;
            m2.Value = window.M2;
            command.ExecuteNonQuery();
        }
    }
}
