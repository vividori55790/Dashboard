using System;
using System.Collections.Generic;
using System.Threading;
using TelemetryDashboard.Core.Collections;

namespace TelemetryDashboard.Infrastructure.Serial;

/// <summary>
/// Holds packets that arrive while a link is down and replays them once it is restored.
/// </summary>
/// <remarks>
/// This is the zero-data-loss half of the auto-healing requirement, and it now lives beside the
/// reconnect engine that owns the link state. It previously sat in <c>Core.Services</c> as an
/// <c>AutoHealingManager</c> that never reconnected anything, so the codebase carried two things
/// named for auto-healing in two layers and neither one did the whole job.
/// <para>
/// The buffer is bounded. When it overflows, the oldest packets are dropped and
/// <see cref="DroppedCount"/> records how many — an outage longer than the buffer is data loss,
/// and the operator is told rather than left to assume the replay was complete.
/// </para>
/// </remarks>
public sealed class ZeroLossPacketBuffer
{
    private readonly RingBuffer<object> _buffer;
    private readonly object _lock = new();
    private long _bufferedTotal;
    private long _droppedCount;

    public ZeroLossPacketBuffer(int capacity = 5000)
    {
        _buffer = new RingBuffer<object>(capacity);
    }

    /// <summary>Whether the link is currently considered up.</summary>
    public bool IsConnected { get; private set; } = true;

    public int Capacity => _buffer.Capacity;

    /// <summary>Packets currently held awaiting replay.</summary>
    public int PendingCount => _buffer.Count;

    /// <summary>Packets buffered across the lifetime of this instance.</summary>
    public long BufferedTotal => Interlocked.Read(ref _bufferedTotal);

    /// <summary>Packets discarded because the outage outlasted the buffer.</summary>
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    public event EventHandler<long>? PacketsDropped;

    public void OnConnectionLost()
    {
        lock (_lock) IsConnected = false;
    }

    public void OnConnectionRestored()
    {
        lock (_lock) IsConnected = true;
    }

    /// <summary>Stores a packet received while the link is down.</summary>
    public void BufferPacketDuringDisconnect(object packet)
    {
        if (packet is null) return;

        lock (_lock)
        {
            bool wasFull = _buffer.IsFull;
            _buffer.Enqueue(packet);
            Interlocked.Increment(ref _bufferedTotal);

            if (wasFull)
            {
                long dropped = Interlocked.Increment(ref _droppedCount);
                PacketsDropped?.Invoke(this, dropped);
            }
        }
    }

    /// <summary>Replays and clears every buffered packet in arrival order.</summary>
    public int FlushBufferedPackets(Action<object> dispatchAction)
    {
        ArgumentNullException.ThrowIfNull(dispatchAction);

        List<object> pending;
        lock (_lock)
        {
            pending = _buffer.Flush();
        }

        foreach (object packet in pending)
        {
            dispatchAction(packet);
        }

        return pending.Count;
    }

    public void Clear()
    {
        lock (_lock) _buffer.Clear();
    }
}
