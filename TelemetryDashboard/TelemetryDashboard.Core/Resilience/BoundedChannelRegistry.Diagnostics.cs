using System.Collections.Generic;

namespace TelemetryDashboard.Core.Resilience;

/// <summary>
/// The read side of <see cref="BoundedChannelRegistry{TState}"/>: what it is holding, what it is
/// allowed to hold, and what it has thrown away.
/// </summary>
/// <remarks>
/// These members exist so the bound is observable while the system is running. A ceiling that no one
/// can read is only marginally better than no ceiling: the process survives, but the operator still
/// finds out that channels were being dropped by noticing that a sensor stopped producing verdicts.
/// </remarks>
public sealed partial class BoundedChannelRegistry<TState> where TState : class
{
    /// <summary>Channels resident right now.</summary>
    public int Count
    {
        get
        {
            int total = 0;
            foreach (ChannelRegistryShard<TState> shard in _shards)
            {
                lock (shard.Gate) total += shard.Count;
            }
            return total;
        }
    }

    /// <summary>How many evicted names remain attributable to a specific channel.</summary>
    public int EvictionRecordCapacity => _evicted.Capacity;

    /// <summary>Number of shards. Exposed so a test can reason about approximate-LRU boundaries.</summary>
    public int ShardCount => _shards.Length;

    public IReadOnlyList<TState> Snapshot()
    {
        var result = new List<TState>();
        foreach (ChannelRegistryShard<TState> shard in _shards)
        {
            lock (shard.Gate) shard.CopyStatesTo(result);
        }
        return result;
    }

    public IReadOnlyList<string> Keys()
    {
        var result = new List<string>();
        foreach (ChannelRegistryShard<TState> shard in _shards)
        {
            lock (shard.Gate) shard.CopyKeysTo(result);
        }
        return result;
    }

    /// <summary>Names evicted recently enough to still be attributable, oldest first.</summary>
    public IReadOnlyList<string> RecentlyEvicted() => _evicted.Snapshot();

    /// <summary>
    /// True only when this name is still inside the retained eviction window. A false answer means
    /// "not attributable", not "never evicted" — see <see cref="ChannelAdmission.Admitted"/>.
    /// </summary>
    public bool WasRecentlyEvicted(string key) => _evicted.Contains(key ?? string.Empty);

    /// <summary>A snapshot an operator or a dashboard can render.</summary>
    public ChannelCardinalityReport Report(string subject) =>
        new(subject, Count, Capacity, Evictions, EvictionRecordCapacity);
}
