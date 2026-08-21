using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>
/// Durable <see cref="IDataLogger"/> backed by a SQLite file: the store telemetry survives in once
/// it has scrolled out of the in-memory <see cref="ChannelDataLogger"/> ring.
/// </summary>
/// <remarks>
/// Nothing here catches <see cref="SqliteException"/>. A full disk, a read-only volume or a corrupt
/// file raises out of the write or query that hit it: a recording that silently stopped persisting
/// looks exactly like a recording with nothing to say, and the operator finds out when they go
/// looking for the data and it is not there.
/// <para>
/// A connection is opened per operation and closed again, so the process holds no handle on the
/// file between calls and the database can be moved or deleted while the application runs.
/// </para>
/// </remarks>
public sealed class SqliteDataLogger : IDataLogger, IDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private long _writtenCount;

    /// <summary>Opens (creating if absent) the log database at <paramref name="databasePath"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="databasePath"/> is blank.</exception>
    /// <exception cref="SqliteException">The file exists but is not a usable SQLite database.</exception>
    public SqliteDataLogger(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path must be provided.", nameof(databasePath));
        }

        DatabasePath = databasePath;
        _connectionString = SqliteTelemetrySchema.BuildConnectionString(databasePath);
        InitializeSchema();
    }

    /// <summary>Path of the SQLite file backing this logger.</summary>
    public string DatabasePath { get; }

    /// <summary>Packets committed by this instance since construction.</summary>
    public long WrittenCount => Interlocked.Read(ref _writtenCount);

    /// <summary>Creates the log table, or throws when the file is not a usable database.</summary>
    /// <remarks>
    /// Called from the constructor and safe to call again. The parent directory is created if it is
    /// missing — a failure to create it still throws, so this cannot mask an unwritable location.
    /// </remarks>
    public void InitializeSchema()
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(DatabasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SqliteTelemetrySchema.CreateTableSql;
        command.ExecuteNonQuery();
    }

    /// <summary>Writes a single packet.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="packet"/> is null.</exception>
    public Task WriteAsync(TelemetryPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return WriteBatchAsync(new[] { packet }, cancellationToken);
    }

    /// <summary>Writes a batch of packets as one atomic unit. See <see cref="SqliteTelemetryWriter"/>.</summary>
    /// <remarks>
    /// Writers are serialised on a single gate. SQLite admits one writer at a time regardless, so
    /// overlapping callers would otherwise queue inside the driver on a busy-retry timer and report
    /// a lock timeout as though the database were held by someone else. The SQLite work is itself
    /// synchronous — the engine has no async I/O — so this blocks the calling thread for the length
    /// of the transaction; call it from the drain loop or a worker, not from a UI thread.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="packets"/> is null.</exception>
    /// <exception cref="ArgumentException">The sequence contains a null element.</exception>
    public async Task WriteBatchAsync(IEnumerable<TelemetryPacket> packets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packets);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            long written = SqliteTelemetryWriter.Insert(connection, packets, cancellationToken);
            Interlocked.Add(ref _writtenCount, written);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Reads back packets matching <paramref name="filter"/>, oldest first.</summary>
    /// <remarks>
    /// Null and empty filter members impose no constraint; see <see cref="SqliteTelemetryQuery"/>
    /// for how the predicate is built and how text is compared.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="filter"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="QueryFilter.Limit"/> is negative.</exception>
    public async Task<IEnumerable<TelemetryPacket>> QueryAsync(QueryFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.Limit < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(filter), filter.Limit, "Limit must not be negative.");
        }

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using SqliteCommand command = connection.CreateCommand();
        SqliteTelemetryQuery.Configure(command, filter);

        var results = new List<TelemetryPacket>();
        using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(SqliteTelemetrySchema.ReadPacket(reader));
        }

        return results;
    }

    /// <summary>Releases the write gate. No file handle is held between operations.</summary>
    public void Dispose() => _writeLock.Dispose();
}
