using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Storage;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>The write path: compress, aggregate, commit — in that order, once per batch.</summary>
public sealed partial class TieredTelemetryStore
{
    /// <summary>Writes a single packet as a one-sample block.</summary>
    /// <remarks>
    /// Correct but wasteful: a block of one sample cannot amortise its header over anything, and
    /// the measured cost of that is in the compression benchmark. Batch through
    /// <see cref="WriteBatchAsync"/> wherever the shape of the data allows it.
    /// </remarks>
    public Task WriteAsync(TelemetryPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return WriteBatchAsync(new[] { packet }, cancellationToken);
    }

    /// <summary>
    /// Compresses, aggregates and commits a batch as one atomic unit.
    /// </summary>
    /// <remarks>
    /// The order matters. Encoding and aggregation happen before the transaction opens, so a
    /// malformed batch — a null packet, say — is rejected without a partial write ever existing,
    /// and the counters are advanced only after the commit returns. A failed batch therefore leaves
    /// the store exactly as it was, which is what lets the drain hold that batch and retry it
    /// without double-counting anything into the rollups.
    /// <para>
    /// Writers are serialised on one gate, as in <see cref="SqliteDataLogger"/>: SQLite admits a
    /// single writer regardless, and queueing inside the driver surfaces as a lock timeout that
    /// reads like a fault rather than like contention.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="packets"/> is null.</exception>
    /// <exception cref="ArgumentException">The sequence contains a null element.</exception>
    public async Task WriteBatchAsync(
        IEnumerable<TelemetryPacket> packets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packets);

        IReadOnlyList<TelemetryPacket> batch =
            packets as IReadOnlyList<TelemetryPacket> ?? packets.ToList();
        if (batch.Count == 0) return;

        IReadOnlyList<CompressedSampleBlock> blocks = TelemetryBlockBuilder.Build(batch);
        var aggregator = new RollupBatchAggregator();
        foreach (TelemetryPacket packet in batch)
        {
            aggregator.Add(ChannelKey.From(packet.NodeId, packet.Variable), packet.Timestamp, packet.Value);
        }

        IReadOnlyList<RollupWindow> windows = aggregator.Windows();

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            TieredStoreWriter.Commit(connection, blocks, windows, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }

        Interlocked.Add(ref _sampleCount, batch.Count);
        Interlocked.Add(ref _blockCount, blocks.Count);
        Interlocked.Add(ref _payloadBytes, blocks.Sum(b => (long)b.PayloadBytes));
        Interlocked.Add(ref _windowMergeCount, windows.Count);
        Interlocked.Add(ref _noReadingCount, aggregator.NoReadingCount);
    }
}
