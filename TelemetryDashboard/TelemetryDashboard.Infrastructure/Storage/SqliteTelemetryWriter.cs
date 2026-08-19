using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Data.Sqlite;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>
/// The batch insert behind <see cref="SqliteDataLogger.WriteBatchAsync"/>.
/// </summary>
/// <remarks>
/// One transaction and one prepared statement with rebound parameters, for both the single-packet
/// and the batch path. Per-packet transactions would mean an fsync per packet, which caps ingest
/// far below the 1 kHz this pipeline is expected to sustain; re-preparing the statement each row
/// re-parses the same SQL thousands of times per second.
/// </remarks>
internal static class SqliteTelemetryWriter
{
    /// <summary>
    /// Inserts every packet inside a single transaction on <paramref name="connection"/> and
    /// returns the number committed.
    /// </summary>
    /// <remarks>
    /// Cancellation mid-batch leaves the transaction uncommitted, so it rolls back on dispose. A
    /// partially applied batch would be worse than none: a later query would read the surviving
    /// prefix as a complete recording with no sign that the rest was ever attempted.
    /// </remarks>
    /// <exception cref="ArgumentException">The sequence contains a null element.</exception>
    /// <exception cref="OperationCanceledException">Cancelled before the commit.</exception>
    internal static long Insert(
        SqliteConnection connection,
        IEnumerable<TelemetryPacket> packets,
        CancellationToken cancellationToken)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = SqliteTelemetrySchema.InsertSql;

        SqliteParameter ticks = command.Parameters.Add("$ticks", SqliteType.Integer);
        SqliteParameter node = command.Parameters.Add("$node", SqliteType.Text);
        SqliteParameter variable = command.Parameters.Add("$variable", SqliteType.Text);
        SqliteParameter value = command.Parameters.Add("$value", SqliteType.Real);
        SqliteParameter unit = command.Parameters.Add("$unit", SqliteType.Text);
        SqliteParameter raw = command.Parameters.Add("$raw", SqliteType.Text);
        SqliteParameter flags = command.Parameters.Add("$flags", SqliteType.Integer);
        command.Prepare();

        long written = 0;
        foreach (TelemetryPacket packet in packets)
        {
            if (packet is null)
            {
                // Skipping it would drop a packet the caller believes was stored.
                throw new ArgumentException(
                    $"Packet at index {written} is null; the batch was not written.", nameof(packets));
            }

            cancellationToken.ThrowIfCancellationRequested();

            ticks.Value = SqliteTelemetrySchema.ToUtcTicks(packet.Timestamp);
            node.Value = packet.NodeId ?? string.Empty;
            variable.Value = packet.Variable ?? string.Empty;
            value.Value = SqliteTelemetrySchema.BindValue(packet.Value);
            unit.Value = packet.Unit ?? string.Empty;
            raw.Value = packet.RawData ?? string.Empty;
            flags.Value = (long)packet.Flags;

            command.ExecuteNonQuery();
            written++;
        }

        transaction.Commit();
        return written;
    }
}
