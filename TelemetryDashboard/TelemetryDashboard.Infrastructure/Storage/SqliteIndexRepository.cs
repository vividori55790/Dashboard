using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>
/// SQLite index over recorded telemetry: fast lookup of which file and offset holds a channel
/// at a given moment, without scanning the archives themselves.
/// </summary>
/// <remarks>
/// A corrupt or non-SQLite file raises <see cref="SqliteException"/> from
/// <see cref="InitializeSchema"/> rather than being silently recreated — quietly replacing a
/// database the operator believes holds history is worse than refusing to open it.
/// </remarks>
public sealed class SqliteIndexRepository
{
    private readonly string _databasePath;

    public SqliteIndexRepository(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path must be provided.", nameof(databasePath));
        }
        _databasePath = databasePath;
    }

    public string DatabasePath => _databasePath;

    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = _databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        // Pooling keeps the OS file handle alive after Dispose, so an operator (or a test) cannot
        // move or delete the database until the process exits. This repository opens per operation,
        // so the pool buys little and costs a locked file.
        Pooling = false
    }.ToString();

    /// <summary>Creates the index tables, or throws when the file is not a usable database.</summary>
    public void InitializeSchema()
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS telemetry_index (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                node_id     TEXT    NOT NULL,
                channel     TEXT    NOT NULL,
                utc_ticks   INTEGER NOT NULL,
                value       REAL    NULL,
                archive     TEXT    NULL,
                byte_offset INTEGER NULL
            );
            CREATE INDEX IF NOT EXISTS ix_index_channel_time
                ON telemetry_index (channel, utc_ticks);
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>Indexes a batch of packets in one transaction.</summary>
    /// <remarks>
    /// Values and timestamps are bound through <see cref="SqliteTelemetrySchema"/> so the index
    /// and the durable store agree on both. That matters most for NaN: a failed sensor reports it
    /// routinely, and binding it directly threw and discarded the entire batch.
    /// </remarks>
    public int IndexPackets(IEnumerable<TelemetryPacket> packets, string? archiveFile = null)
    {
        if (packets is null) return 0;

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO telemetry_index (node_id, channel, utc_ticks, value, archive)
            VALUES ($node, $channel, $ticks, $value, $archive);
            """;

        SqliteParameter node = command.Parameters.Add("$node", SqliteType.Text);
        SqliteParameter channel = command.Parameters.Add("$channel", SqliteType.Text);
        SqliteParameter ticks = command.Parameters.Add("$ticks", SqliteType.Integer);
        SqliteParameter value = command.Parameters.Add("$value", SqliteType.Real);
        SqliteParameter archive = command.Parameters.Add("$archive", SqliteType.Text);

        int written = 0;
        foreach (TelemetryPacket packet in packets)
        {
            if (packet is null) continue;

            node.Value = packet.NodeId ?? string.Empty;
            channel.Value = packet.Variable ?? string.Empty;
            ticks.Value = SqliteTelemetrySchema.ToUtcTicks(packet.Timestamp);
            value.Value = SqliteTelemetrySchema.BindValue(packet.Value);
            archive.Value = (object?)archiveFile ?? DBNull.Value;

            command.ExecuteNonQuery();
            written++;
        }

        transaction.Commit();
        return written;
    }

    /// <summary>Counts indexed rows for a channel within a time range.</summary>
    public long CountInRange(string channel, DateTime fromUtc, DateTime toUtc)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*) FROM telemetry_index
            WHERE channel = $channel AND utc_ticks BETWEEN $from AND $to;
            """;
        command.Parameters.AddWithValue("$channel", channel ?? string.Empty);
        command.Parameters.AddWithValue("$from", SqliteTelemetrySchema.ToUtcTicks(fromUtc));
        command.Parameters.AddWithValue("$to", SqliteTelemetrySchema.ToUtcTicks(toUtc));

        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }
}
