using System;
using Microsoft.Data.Sqlite;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>
/// Table layout, connection settings and row mapping shared by <see cref="SqliteDataLogger"/>.
/// </summary>
/// <remarks>
/// Split out of the logger so the mapping rules — the part that decides whether a packet survives
/// a round trip — are stated once instead of twice, once in the insert and once in the reader.
/// Two copies drift, and the drift shows up as a field that silently reads back wrong.
/// </remarks>
internal static class SqliteTelemetrySchema
{
    /// <summary>Column list, in the order <see cref="ReadPacket"/> expects to find them.</summary>
    internal const string Columns = "utc_ticks, node_id, variable, value, unit, raw_data, flags";

    /// <summary>Creates the log table. Idempotent; safe to run against an existing database.</summary>
    /// <remarks>
    /// <c>id</c> is a plain rowid alias rather than AUTOINCREMENT: AUTOINCREMENT maintains an extra
    /// <c>sqlite_sequence</c> row on every insert, which this ingest path pays for at over 1 kHz and
    /// gets nothing back for — the log never reuses or references ids.
    /// <para>
    /// The <c>value</c> column is nullable on purpose; see <see cref="BindValue"/>.
    /// </para>
    /// </remarks>
    internal const string CreateTableSql =
        """
        CREATE TABLE IF NOT EXISTS telemetry_log (
            id        INTEGER PRIMARY KEY,
            utc_ticks INTEGER NOT NULL,
            node_id   TEXT    NOT NULL,
            variable  TEXT    NOT NULL,
            value     REAL    NULL,
            unit      TEXT    NOT NULL,
            raw_data  TEXT    NOT NULL,
            flags     INTEGER NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_log_time
            ON telemetry_log (utc_ticks);
        CREATE INDEX IF NOT EXISTS ix_log_node_var_time
            ON telemetry_log (node_id, variable, utc_ticks);
        """;

    /// <summary>Insert used by both the single and the batch write path.</summary>
    internal const string InsertSql =
        """
        INSERT INTO telemetry_log (utc_ticks, node_id, variable, value, unit, raw_data, flags)
        VALUES ($ticks, $node, $variable, $value, $unit, $raw, $flags);
        """;

    /// <summary>Builds the connection string used for every connection this store opens.</summary>
    internal static string BuildConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Pooling keeps the OS file handle alive after Dispose, so an operator (or a test)
            // cannot move or delete the database until the process exits. This store opens per
            // operation, so the pool buys little and costs a locked file.
            Pooling = false,
            // Microsoft.Data.Sqlite retries SQLITE_BUSY for this long before surfacing it. A drain
            // loop writing while an operator runs a query is normal here; without a wait window
            // that ordinary overlap would raise "database is locked" and look like a real fault.
            DefaultTimeout = 30
        }.ToString();

    /// <summary>Converts a packet timestamp to the UTC tick count stored in <c>utc_ticks</c>.</summary>
    /// <remarks>
    /// <see cref="DateTimeKind.Unspecified"/> is taken as already-UTC rather than local.
    /// <see cref="DateTime.ToUniversalTime"/> would shift it by whatever offset the machine happens
    /// to be in, inventing a time zone the caller never stated and making the same file read back
    /// differently on a differently configured machine.
    /// </remarks>
    internal static long ToUtcTicks(DateTime timestamp) => timestamp.Kind switch
    {
        DateTimeKind.Local => timestamp.ToUniversalTime().Ticks,
        _ => timestamp.Ticks
    };

    /// <summary>Converts a packet value into the object bound to the <c>$value</c> parameter.</summary>
    /// <remarks>
    /// NaN is bound as NULL deliberately. SQLite has no NaN in its REAL type and collapses a bound
    /// NaN to NULL regardless; making that explicit lets <see cref="ReadPacket"/> map NULL back to
    /// NaN, so a failed sensor reading survives the round trip instead of arriving as 0 and being
    /// plotted as a real measurement.
    /// </remarks>
    internal static object BindValue(double value) =>
        double.IsNaN(value) ? DBNull.Value : value;

    /// <summary>Materialises one row into a packet.</summary>
    internal static TelemetryPacket ReadPacket(SqliteDataReader reader) => new()
    {
        Timestamp = new DateTime(reader.GetInt64(0), DateTimeKind.Utc),
        NodeId = reader.GetString(1),
        Variable = reader.GetString(2),
        Value = reader.IsDBNull(3) ? double.NaN : reader.GetDouble(3),
        Unit = reader.GetString(4),
        RawData = reader.GetString(5),
        Flags = (PacketFlags)reader.GetInt64(6)
    };
}
