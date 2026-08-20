using System.Collections.Generic;

namespace TelemetryDashboard.Core.Resilience;

/// <summary>
/// One lock-striped partition of a <see cref="BoundedChannelRegistry{TState}"/>: a dictionary with
/// an intrusive least-recently-used list threaded through its entries.
/// </summary>
/// <remarks>
/// The LRU links live on the entry object rather than in a side <c>LinkedList</c>, so ordering costs
/// two reference fields per channel and no extra allocation per channel. Ordering is exact within a
/// shard and only approximate across shards, which is the price of not taking a process-wide lock on
/// a path that a thousand machines are feeding concurrently.
/// </remarks>
internal sealed class ChannelRegistryShard<TState> where TState : class
{
    internal sealed class Entry
    {
        public string Key = string.Empty;
        public TState State = null!;
        public Entry? Older;
        public Entry? Newer;
    }

    internal readonly object Gate = new();

    private readonly Dictionary<string, Entry> _entries;
    private readonly int _capacity;
    private Entry? _lru;
    private Entry? _mru;

    internal ChannelRegistryShard(int capacity, IEqualityComparer<string> comparer)
    {
        _capacity = capacity;
        _entries = new Dictionary<string, Entry>(comparer);
    }

    internal int Capacity => _capacity;

    /// <summary>Caller must hold <see cref="Gate"/>.</summary>
    internal int Count => _entries.Count;

    /// <summary>Caller must hold <see cref="Gate"/>.</summary>
    internal bool TryGet(string key, out TState? state)
    {
        if (_entries.TryGetValue(key, out Entry? entry))
        {
            Touch(entry);
            state = entry.State;
            return true;
        }

        state = null;
        return false;
    }

    /// <summary>
    /// Caller must hold <see cref="Gate"/>. Inserts <paramref name="state"/> under
    /// <paramref name="key"/>, evicting the least recently used entry if the shard is full.
    /// </summary>
    /// <returns>The key that was evicted to make room, or <c>null</c> if nothing was evicted.</returns>
    internal string? Insert(string key, TState state)
    {
        if (_entries.TryGetValue(key, out Entry? existing))
        {
            existing.State = state;
            Touch(existing);
            return null;
        }

        string? evictedKey = null;
        if (_capacity > 0 && _entries.Count >= _capacity)
        {
            Entry? victim = _lru;
            if (victim is not null)
            {
                Detach(victim);
                _entries.Remove(victim.Key);
                evictedKey = victim.Key;
            }
        }

        var entry = new Entry { Key = key, State = state };
        _entries[key] = entry;
        AppendMru(entry);
        return evictedKey;
    }

    /// <summary>Caller must hold <see cref="Gate"/>.</summary>
    internal bool Remove(string key)
    {
        if (!_entries.TryGetValue(key, out Entry? entry)) return false;

        Detach(entry);
        _entries.Remove(key);
        return true;
    }

    /// <summary>Caller must hold <see cref="Gate"/>.</summary>
    internal void Clear()
    {
        _entries.Clear();
        _lru = null;
        _mru = null;
    }

    /// <summary>Caller must hold <see cref="Gate"/>.</summary>
    internal void CopyStatesTo(List<TState> destination)
    {
        foreach (Entry entry in _entries.Values) destination.Add(entry.State);
    }

    /// <summary>Caller must hold <see cref="Gate"/>.</summary>
    internal void CopyKeysTo(List<string> destination)
    {
        foreach (string key in _entries.Keys) destination.Add(key);
    }

    private void Touch(Entry entry)
    {
        if (ReferenceEquals(_mru, entry)) return;
        Detach(entry);
        AppendMru(entry);
    }

    private void Detach(Entry entry)
    {
        if (entry.Older is not null) entry.Older.Newer = entry.Newer;
        else if (ReferenceEquals(_lru, entry)) _lru = entry.Newer;

        if (entry.Newer is not null) entry.Newer.Older = entry.Older;
        else if (ReferenceEquals(_mru, entry)) _mru = entry.Older;

        entry.Older = null;
        entry.Newer = null;
    }

    private void AppendMru(Entry entry)
    {
        entry.Older = _mru;
        entry.Newer = null;

        if (_mru is not null) _mru.Newer = entry;
        else _lru = entry;

        _mru = entry;
    }
}
