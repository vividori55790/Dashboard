using Microsoft.Data.Sqlite;
using TelemetryDashboard.Core.Storage;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>
/// Table layout for the tiered store: compressed raw blocks, rollup windows, and a prune log.
/// </summary>
/// <remarks>
/// The connection settings are deliberately the ones <see cref="SqliteTelemetrySchema"/> already
/// uses for the row-per-sample store — same journal mode, same synchronous setting, same busy
/// timeout. Turning on WAL here would make this store look faster than the baseline for a reason
/// that has nothing to do with its design, and the baseline could have had it too. Whatever
/// difference the benchmarks show is therefore attributable to the layout: blocks instead of rows,
/// and aggregates maintained on arrival.
/// </remarks>
internal static class TieredStoreSchema
{
    /// <summary>Creates every table and index. Idempotent.</summary>
    /// <remarks>
    /// <c>rollup_window</c> is <c>WITHOUT ROWID</c>: its primary key <em>is</em> the identity of a
    /// bucket, and the upsert that merges a batch into an existing window looks it up on every
    /// write. A rowid table would keep a second B-tree and pay for it on every merge.
    /// <para>
    /// <c>raw_block</c> is indexed by <c>(node_id, variable, end_ticks)</c>, which is what a range
    /// query filters on, and separately by <c>end_ticks</c> alone, which is what retention scans.
    /// </para>
    /// </remarks>
    internal const string CreateSql =
        """
        CREATE TABLE IF NOT EXISTS raw_block (
            id           INTEGER PRIMARY KEY,
            node_id      TEXT    NOT NULL,
            variable     TEXT    NOT NULL,
            unit         TEXT    NOT NULL,
            start_ticks  INTEGER NOT NULL,
            end_ticks    INTEGER NOT NULL,
            sample_count INTEGER NOT NULL,
            codec        INTEGER NOT NULL,
            payload      BLOB    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_block_channel_time
            ON raw_block (node_id, variable, end_ticks);
        CREATE INDEX IF NOT EXISTS ix_block_end
            ON raw_block (end_ticks);

        CREATE TABLE IF NOT EXISTS rollup_window (
            node_id       TEXT    NOT NULL,
            variable      TEXT    NOT NULL,
            interval_code INTEGER NOT NULL,
            bucket_ticks  INTEGER NOT NULL,
            sample_count  INTEGER NOT NULL,
            min_value     REAL    NOT NULL,
            max_value     REAL    NOT NULL,
            sum_value     REAL    NOT NULL,
            m2            REAL    NOT NULL,
            PRIMARY KEY (node_id, variable, interval_code, bucket_ticks)
        ) WITHOUT ROWID;

        CREATE TABLE IF NOT EXISTS retention_log (
            id             INTEGER PRIMARY KEY,
            executed_ticks INTEGER NOT NULL,
            applied        INTEGER NOT NULL,
            detail         TEXT    NOT NULL
        );
        """;

    /// <summary>Same connection settings as the row store, for the reason given on the type.</summary>
    internal static string BuildConnectionString(string databasePath) =>
        SqliteTelemetrySchema.BuildConnectionString(databasePath);

    /// <summary>Materialises one <c>raw_block</c> row.</summary>
    internal static CompressedSampleBlock ReadBlock(SqliteDataReader reader) => new(
        new ChannelKey(reader.GetString(0), reader.GetString(1)),
        reader.GetString(2),
        reader.GetInt64(3),
        reader.GetInt64(4),
        reader.GetInt32(5),
        (byte[])reader.GetValue(6));

    /// <summary>Column list for <see cref="ReadBlock"/>, in the order it expects.</summary>
    internal const string BlockColumns =
        "node_id, variable, unit, start_ticks, end_ticks, sample_count, payload";

    /// <summary>Materialises one <c>rollup_window</c> row.</summary>
    /// <remarks>
    /// The <see cref="RollupWindow"/> constructor rejects a zero count, so a corrupted or
    /// hand-edited row that claims a window with no measurements behind it fails here rather than
    /// being served to a caller as a real average.
    /// </remarks>
    internal static RollupWindow ReadWindow(SqliteDataReader reader, RollupInterval interval) => new(
        new ChannelKey(reader.GetString(0), reader.GetString(1)),
        interval,
        reader.GetInt64(2),
        reader.GetInt64(3),
        reader.GetDouble(4),
        reader.GetDouble(5),
        reader.GetDouble(6),
        reader.GetDouble(7));

    /// <summary>Column list for <see cref="ReadWindow"/>, in the order it expects.</summary>
    internal const string WindowColumns =
        "node_id, variable, bucket_ticks, sample_count, min_value, max_value, sum_value, m2";
}
