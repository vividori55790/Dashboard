namespace TelemetryDashboard.Core.Resilience;

/// <summary>
/// Direct reads, writes and removals on <see cref="BoundedChannelRegistry{TState}"/>, for callers
/// that already have the state in hand and are not going through the admission path.
/// </summary>
/// <remarks>
/// Every one of these routes through the owning shard's lock, and every insertion can evict, so
/// <see cref="BoundedChannelRegistry{TState}.Set"/> is subject to the same ceiling as
/// <see cref="BoundedChannelRegistry{TState}.GetOrAdd"/>. There is deliberately no way to add a
/// channel that bypasses the bound.
/// </remarks>
public sealed partial class BoundedChannelRegistry<TState> where TState : class
{
    /// <summary>Inserts or replaces the state for <paramref name="key"/> and marks it most recently used.</summary>
    public void Set(string key, TState state)
    {
        key ??= string.Empty;
        ChannelRegistryShard<TState> shard = ShardFor(key);
        string? evictedKey;

        lock (shard.Gate) evictedKey = shard.Insert(key, state);

        if (evictedKey is not null) OnEvicted(evictedKey);
    }

    /// <summary>Reads the state for <paramref name="key"/>, marking it most recently used on a hit.</summary>
    public bool TryGet(string key, out TState? state)
    {
        ChannelRegistryShard<TState> shard = ShardFor(key ??= string.Empty);
        lock (shard.Gate) return shard.TryGet(key, out state);
    }

    /// <summary>
    /// Drops one channel. This is a caller-requested removal, not an eviction, so it does not count
    /// toward <see cref="BoundedChannelRegistry{TState}.Evictions"/> and does not enter the eviction
    /// record — conflating the two would let a routine reset look like the ceiling being hit.
    /// </summary>
    public bool Remove(string key)
    {
        ChannelRegistryShard<TState> shard = ShardFor(key ??= string.Empty);
        lock (shard.Gate) return shard.Remove(key);
    }

    /// <summary>Drops every channel. Does not reset <see cref="Evictions"/>, which is a lifetime total.</summary>
    public void Clear()
    {
        foreach (ChannelRegistryShard<TState> shard in _shards)
        {
            lock (shard.Gate) shard.Clear();
        }
        _evicted.Clear();
    }
}
