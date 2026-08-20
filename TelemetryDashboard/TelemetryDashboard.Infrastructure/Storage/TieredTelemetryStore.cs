using System;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Storage;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>
/// Durable store that keeps raw samples as compressed blocks and maintains rollups as data arrives.
/// </summary>
/// <remarks>
/// A drop-in <see cref="IDataLogger"/>, so it can take the place of <see cref="SqliteDataLogger"/>
/// behind <see cref="ChannelDataLoggerDrain"/>. What it changes is the layout: one row per batch
/// per channel instead of one row per sample, and one merged aggregate row per bucket per tier,
/// maintained on the write path rather than by a later pass over the raw data. Measured figures for
/// both stores are in the <c>TieredStorage</c>, <c>Rollup</c> and <c>CompressionBench</c>
/// benchmarks; nothing here is claimed from theory.
/// <para>
/// What a block does not carry is <see cref="TelemetryPacket.RawData"/>. Timestamp, value, flags
/// and unit survive exactly; the original wire text does not, because it is most of what makes a
/// row in the raw log expensive. A caller that needs the text keeps the row store as well.
/// </para>
/// <para>
/// Exceptions are not caught, on the same principle as <see cref="SqliteDataLogger"/>: a store that
/// silently stopped persisting looks exactly like a quiet sensor.
/// </para>
/// </remarks>
public sealed partial class TieredTelemetryStore : IDataLogger, IDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private long _sampleCount;
    private long _blockCount;
    private long _payloadBytes;
    private long _windowMergeCount;
    private long _noReadingCount;

    /// <summary>Opens (creating if absent) the tiered store at <paramref name="databasePath"/>.</summary>
    /// <param name="databasePath">File backing the store.</param>
    /// <param name="retention">
    /// Retention policy. Omitted means <see cref="RetentionPolicy.Disabled"/>: nothing is ever
    /// deleted until a caller both supplies an enabled policy and asks for a prune.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="databasePath"/> is blank.</exception>
    public TieredTelemetryStore(string databasePath, RetentionPolicy? retention = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path must be provided.", nameof(databasePath));
        }

        DatabasePath = databasePath;
        Retention = (retention ?? RetentionPolicy.Disabled).Validated();
        _connectionString = TieredStoreSchema.BuildConnectionString(databasePath);
        InitializeSchema();
    }

    /// <summary>Path of the SQLite file backing this store.</summary>
    public string DatabasePath { get; }

    /// <summary>The policy this store prunes by. Disabled unless the caller supplied one.</summary>
    public RetentionPolicy Retention { get; }

    /// <summary>Samples committed by this instance, NaN readings included.</summary>
    public long WrittenSampleCount => Interlocked.Read(ref _sampleCount);

    /// <summary>Compressed blocks committed.</summary>
    public long WrittenBlockCount => Interlocked.Read(ref _blockCount);

    /// <summary>Total compressed payload committed, in bytes. Over the sample count, this is the ratio.</summary>
    public long CompressedByteCount => Interlocked.Read(ref _payloadBytes);

    /// <summary>Rollup windows merged into storage. Far smaller than the sample count, by design.</summary>
    public long MergedWindowCount => Interlocked.Read(ref _windowMergeCount);

    /// <summary>Samples that were NaN, and so were stored raw but aggregated into nothing.</summary>
    public long NoReadingCount => Interlocked.Read(ref _noReadingCount);

    /// <summary>Creates the tables. Idempotent; safe to call against an existing store.</summary>
    public void InitializeSchema()
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(DatabasePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = TieredStoreSchema.CreateSql;
        command.ExecuteNonQuery();
    }

    /// <summary>Releases the write gate. No file handle is held between operations.</summary>
    public void Dispose() => _writeLock.Dispose();
}
