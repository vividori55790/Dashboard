using System.Collections.Generic;
using System.Threading;

namespace TelemetryDashboard.Core.Resilience;

/// <summary>
/// Per-channel state with a declared ceiling and a visible eviction policy.
/// </summary>
/// <remarks>
/// <para><b>Why a ceiling.</b> The stores this replaces were <c>ConcurrentDictionary</c> instances
/// that never removed anything, so their size was set by how many distinct channel names the process
/// had ever seen. That is not a tuning parameter, it is the absence of one: there was no figure an
/// operator could read, none they could set, and no symptom before the process died. Measured on
/// this tree, per-channel cost is about 690 bytes in the analytics engine and 275 bytes in the
/// circuit breaker, so a ceiling is what turns "eventually fatal" into a number.</para>
///
/// <para><b>Why least-recently-updated.</b> The state being protected is a rolling window of recent
/// samples; its whole value is recency, so the least recently updated channel is by construction the
/// one whose retained statistics have decayed most. Three alternatives were rejected: insertion
/// order evicts the long-lived channels that have been streaming healthily for a week, which is
/// backwards; random eviction is cheaper but will drop a channel an operator is actively watching;
/// least-frequently-used needs a counter per channel and clings to channels that were busy last
/// month. LRU also has a property that matters specifically to the circuit breaker: a flooding
/// channel is by definition the most recently used one, so LRU cannot evict the flooder and hand it
/// a clean rate window.</para>
///
/// <para><b>Approximation.</b> Ordering is exact inside a shard and approximate across shards, so a
/// victim is the least recently used of its partition rather than of the whole registry. Exact
/// global LRU needs one lock shared by every ingest thread. The ceiling itself is not approximate:
/// shard capacities sum to <see cref="Capacity"/>, so <see cref="Count"/> cannot exceed it.</para>
/// </remarks>
public sealed partial class BoundedChannelRegistry<TState> where TState : class
{
    private const int MinEntriesPerShard = 64;
    private const int MaxShards = 64;

    private readonly ChannelRegistryShard<TState>[] _shards;
    private readonly IEqualityComparer<string> _comparer;
    private readonly ChannelEvictionRecord _evicted;
    private readonly int _shardMask;
    private long _evictions;

    /// <param name="capacity">Hard ceiling on resident channels. Must be positive.</param>
    /// <param name="comparer">Key comparer; defaults to <see cref="StringComparer.OrdinalIgnoreCase"/>.</param>
    /// <param name="evictionRecordCapacity">
    /// How many evicted names stay attributable. Negative selects a sixteenth of the ceiling clamped
    /// to [64, 4096]: enough to explain a burst of churn, small enough that the diagnostics do not
    /// become a second unbounded store.
    /// </param>
    public BoundedChannelRegistry(int capacity, IEqualityComparer<string>? comparer = null, int evictionRecordCapacity = -1)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "A ceiling of zero or less admits nothing.");

        Capacity = capacity;
        _comparer = comparer ?? StringComparer.OrdinalIgnoreCase;

        int shardCount = 1;
        while (shardCount < MaxShards && capacity / (shardCount * 2) >= MinEntriesPerShard) shardCount *= 2;
        _shardMask = shardCount - 1;

        _shards = new ChannelRegistryShard<TState>[shardCount];
        int baseSize = capacity / shardCount;
        int remainder = capacity % shardCount;
        for (int i = 0; i < shardCount; i++)
        {
            _shards[i] = new ChannelRegistryShard<TState>(baseSize + (i < remainder ? 1 : 0), _comparer);
        }

        _evicted = new ChannelEvictionRecord(
            evictionRecordCapacity >= 0 ? evictionRecordCapacity : Math.Clamp(capacity / 16, 64, 4096),
            _comparer);
    }

    /// <summary>The declared ceiling. <see cref="Count"/> never exceeds this.</summary>
    public int Capacity { get; }

    /// <summary>Total channels discarded since construction. Exact, and never reset by eviction.</summary>
    public long Evictions => Interlocked.Read(ref _evictions);

    /// <summary>Raised after a channel's state is discarded. Fired outside the registry's locks.</summary>
    public event EventHandler<string>? Evicted;

    private ChannelRegistryShard<TState> ShardFor(string key) =>
        _shards[_comparer.GetHashCode(key) & _shardMask];

    /// <summary>
    /// Returns the state for <paramref name="key"/>, creating it if absent, and reports through
    /// <paramref name="admission"/> whether this call rebuilt state that had been evicted.
    /// </summary>
    public TState GetOrAdd(string key, Func<string, TState> factory, out ChannelAdmission admission)
    {
        ArgumentNullException.ThrowIfNull(factory);
        key ??= string.Empty;

        ChannelRegistryShard<TState> shard = ShardFor(key);
        string? evictedKey;
        TState state;

        lock (shard.Gate)
        {
            if (shard.TryGet(key, out TState? existing))
            {
                admission = ChannelAdmission.Existing;
                return existing!;
            }

            state = factory(key);
            evictedKey = shard.Insert(key, state);
        }

        admission = _evicted.Contains(key)
            ? ChannelAdmission.ReadmittedAfterEviction
            : ChannelAdmission.Admitted;

        if (evictedKey is not null) OnEvicted(evictedKey);
        return state;
    }

    private void OnEvicted(string key)
    {
        Interlocked.Increment(ref _evictions);
        _evicted.Note(key);
        Evicted?.Invoke(this, key);
    }
}
