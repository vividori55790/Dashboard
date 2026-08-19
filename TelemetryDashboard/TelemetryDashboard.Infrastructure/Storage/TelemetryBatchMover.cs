using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>
/// Moves one batch at a time from a <see cref="ChannelDataLogger"/> ring into an
/// <see cref="IDataLogger"/>, holding the in-flight batch across a failed attempt.
/// </summary>
/// <remarks>
/// Separated from <see cref="ChannelDataLoggerDrain"/>, which owns only the background loop and its
/// lifetime, because the custody rule lives here: once <see cref="ChannelDataLogger.TryRead"/> hands
/// a packet over, the ring no longer has it and this batch is the only copy. Everything below is
/// arranged so that a batch is cleared after the write commits and never before.
/// </remarks>
internal sealed class TelemetryBatchMover
{
    private readonly ChannelDataLogger _source;
    private readonly IDataLogger _sink;
    private readonly int _batchSize;
    private readonly List<TelemetryPacket> _pending;
    private long _drainedCount;
    private long _batchCount;

    internal TelemetryBatchMover(ChannelDataLogger source, IDataLogger sink, int batchSize)
    {
        _source = source;
        _sink = sink;
        _batchSize = batchSize;
        _pending = new List<TelemetryPacket>(batchSize);
    }

    /// <summary>Largest number of packets one call will commit.</summary>
    internal int BatchSize => _batchSize;

    /// <summary>Packets taken from the ring but not yet committed.</summary>
    internal int PendingCount => _pending.Count;

    /// <summary>Packets committed so far.</summary>
    internal long DrainedCount => Interlocked.Read(ref _drainedCount);

    /// <summary>Batches committed so far.</summary>
    internal long BatchCount => Interlocked.Read(ref _batchCount);

    /// <summary>
    /// Tops the batch up from the ring, writes it, and returns how many packets were committed.
    /// Returns zero when the ring and the held batch are both empty.
    /// </summary>
    /// <remarks>
    /// The retained batch is topped up rather than replaced, so a retry after a failed write keeps
    /// the original arrival order instead of interleaving it with packets that arrived since.
    /// </remarks>
    internal async Task<int> FlushOnceAsync(CancellationToken cancellationToken)
    {
        while (_pending.Count < _batchSize && _source.TryRead(out TelemetryPacket? packet))
        {
            if (packet is not null) _pending.Add(packet);
        }

        if (_pending.Count == 0) return 0;

        await _sink.WriteBatchAsync(_pending, cancellationToken).ConfigureAwait(false);

        int written = _pending.Count;
        _pending.Clear();
        Interlocked.Add(ref _drainedCount, written);
        Interlocked.Increment(ref _batchCount);
        return written;
    }
}
