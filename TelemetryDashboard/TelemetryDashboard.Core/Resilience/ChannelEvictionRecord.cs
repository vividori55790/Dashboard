using System.Collections.Generic;

namespace TelemetryDashboard.Core.Resilience;

/// <summary>
/// A bounded record of which channel names were most recently evicted.
/// </summary>
/// <remarks>
/// The obvious way to report "this channel is back after being evicted" is to keep the set of every
/// name ever evicted — which re-creates, in the reporting path, exactly the unbounded growth the
/// registry exists to prevent. So this keeps a fixed-size ring instead: the newest evictions
/// displace the oldest, and a name that rolls off the end can no longer be attributed.
/// <para>
/// The consequence is stated rather than hidden: this structure can answer "yes, evicted recently"
/// with certainty and can never answer "no, never evicted" with certainty. Callers that need an
/// exact figure use the registry's eviction counter, which is a monotonic count and is never lossy.
/// </para>
/// </remarks>
internal sealed class ChannelEvictionRecord
{
    private readonly object _gate = new();
    private readonly string[] _ring;
    private readonly HashSet<string> _present;
    private int _cursor;

    internal ChannelEvictionRecord(int capacity, IEqualityComparer<string> comparer)
    {
        Capacity = Math.Max(0, capacity);
        _ring = new string[Capacity];
        _present = new HashSet<string>(comparer);
    }

    internal int Capacity { get; }

    /// <summary>
    /// Notes that <paramref name="key"/> was evicted. A name already in the record keeps its
    /// original slot, so the ring and the membership set stay one-to-one and a name that is evicted
    /// repeatedly cannot crowd the record with duplicates of itself.
    /// </summary>
    internal void Note(string key)
    {
        if (Capacity == 0) return;

        lock (_gate)
        {
            if (!_present.Add(key)) return;

            string? displaced = _ring[_cursor];
            if (displaced is not null) _present.Remove(displaced);

            _ring[_cursor] = key;
            _cursor = (_cursor + 1) % Capacity;
        }
    }

    /// <summary>True only when this name is still inside the retained window of evictions.</summary>
    internal bool Contains(string key)
    {
        if (Capacity == 0) return false;

        lock (_gate)
        {
            return _present.Contains(key);
        }
    }

    /// <summary>The retained eviction names, oldest first.</summary>
    internal IReadOnlyList<string> Snapshot()
    {
        if (Capacity == 0) return Array.Empty<string>();

        lock (_gate)
        {
            var result = new List<string>(_present.Count);
            for (int i = 0; i < Capacity; i++)
            {
                string? key = _ring[(_cursor + i) % Capacity];
                if (key is not null && _present.Contains(key)) result.Add(key);
            }
            return result;
        }
    }

    internal void Clear()
    {
        if (Capacity == 0) return;

        lock (_gate)
        {
            Array.Clear(_ring, 0, _ring.Length);
            _present.Clear();
            _cursor = 0;
        }
    }
}
