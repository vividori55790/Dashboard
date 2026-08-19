using System;
using System.Threading;
using TelemetryDashboard.Core.Collections;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>
/// Bounded producer/consumer buffer between high-rate ingest and slower disk writes.
/// </summary>
/// <remarks>
/// Backed by the shared <see cref="RingBuffer{T}"/>. When ingest outruns the writer the oldest
/// packets are evicted and counted in <see cref="DroppedCount"/> — an overflowing logger is data
/// loss, and it must be visible rather than inferred from a gap in the output file.
/// </remarks>
public sealed class ChannelDataLogger
{
    private readonly RingBuffer<TelemetryPacket> _buffer;
    private readonly object _lock = new();
    private long _droppedCount;
    private long _enqueuedCount;

    public ChannelDataLogger(int capacity = 10_000)
    {
        _buffer = new RingBuffer<TelemetryPacket>(capacity);
    }

    public int Capacity => _buffer.Capacity;

    /// <summary>Packets waiting to be written.</summary>
    public int PendingCount => _buffer.Count;

    /// <summary>Packets discarded because the buffer was full.</summary>
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    /// <summary>Packets accepted over the lifetime of this logger.</summary>
    public long EnqueuedCount => Interlocked.Read(ref _enqueuedCount);

    /// <summary>Raised when a packet is evicted, carrying the running drop total.</summary>
    public event EventHandler<long>? PacketDropped;

    /// <summary>
    /// Queues a packet. Returns false when the queue was already full and an older packet had to
    /// be evicted to make room.
    /// </summary>
    public bool TryEnqueue(TelemetryPacket packet)
    {
        if (packet is null) return false;

        bool evicted;
        lock (_lock)
        {
            evicted = _buffer.IsFull;
            _buffer.Enqueue(packet);
        }

        Interlocked.Increment(ref _enqueuedCount);

        if (evicted)
        {
            long total = Interlocked.Increment(ref _droppedCount);
            PacketDropped?.Invoke(this, total);
            return false;
        }

        return true;
    }

    /// <summary>Takes the oldest queued packet.</summary>
    public bool TryRead(out TelemetryPacket? packet)
    {
        lock (_lock)
        {
            if (_buffer.TryDequeue(out TelemetryPacket? item))
            {
                packet = item;
                return true;
            }
        }

        packet = null;
        return false;
    }

    public void Clear()
    {
        lock (_lock) _buffer.Clear();
    }
}
