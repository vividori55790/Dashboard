using System;

namespace TelemetryDashboard.Core.Resilience;

/// <summary>
/// What the breaker is holding per channel, and what it is allowed to hold.
/// </summary>
/// <remarks>
/// The breaker's memory has two axes and only one of them is capped by the channel ceiling.
/// Cardinality is bounded by <see cref="TelemetryCircuitBreaker.MaxTrackedChannels"/>. Rate is not:
/// each admitted packet enqueues one timestamp on its channel's tracker, and those entries only
/// leave when the sliding window ages them out, so a channel receiving <em>r</em> packets a second
/// holds about <em>r</em> timestamps. Measured on this tree, a queued timestamp costs about 10 bytes
/// once queue slack is counted (10.0 bytes/packet measured at 100 packets/channel, 11.8 at 20).
/// So the honest ceiling is roughly <c>MaxTrackedChannels x observed-rate x 10 bytes</c>, and the
/// rate term is set by the field, not by configuration. <see cref="TelemetryCircuitBreaker.QueuedTimestampCount"/>
/// is what makes that term observable instead of theoretical.
/// </remarks>
public partial class TelemetryCircuitBreaker
{
    /// <summary>The declared ceiling on channels with rate history.</summary>
    public int MaxTrackedChannels => _channelTrackers.Capacity;

    /// <summary>Channels with resident rate history right now.</summary>
    public int TrackedChannelCount => _channelTrackers.Count;

    /// <summary>Channels currently isolated.</summary>
    public int IsolatedChannelCount => _isolatedChannels.Count;

    /// <summary>
    /// Channels whose rate history has been discarded to stay within the ceiling.
    /// </summary>
    /// <remarks>
    /// A non-zero value here has a safety meaning, not just a memory one: an evicted channel's rate
    /// window restarts empty, so its next packets are counted from zero and it needs to exceed the
    /// limit again before it can be isolated. Eviction is least-recently-used, which is the specific
    /// policy that makes this survivable — a channel flooding hard is the most recently used channel
    /// in its shard, so it is the last candidate for eviction, not the first.
    /// </remarks>
    public long ChannelEvictions => _channelTrackers.Evictions;

    /// <summary>Isolation records dropped to stay within the ceiling.</summary>
    public long IsolationEvictions => _isolatedChannels.Evictions;

    /// <summary>Occupancy of the rate-tracking store, for an operator watching the limit approach.</summary>
    public ChannelCardinalityReport TrackerCardinality => _channelTrackers.Report("breaker rate trackers");

    /// <summary>Occupancy of the isolation store.</summary>
    public ChannelCardinalityReport IsolationCardinality => _isolatedChannels.Report("breaker isolations");

    /// <summary>Raised when a channel's rate history is discarded to stay within the ceiling.</summary>
    public event EventHandler<string>? ChannelEvicted
    {
        add => _channelTrackers.Evicted += value;
        remove => _channelTrackers.Evicted -= value;
    }

    /// <summary>
    /// Timestamps currently buffered across all trackers: the breaker's rate-driven memory, in
    /// packets.
    /// </summary>
    /// <remarks>
    /// A diagnostic, not a per-frame reading. It walks every resident tracker and takes each lock,
    /// the same cost as <see cref="IsUiResourceClamped"/>. It does not prune, so it reports what is
    /// actually retained rather than what would survive a prune.
    /// </remarks>
    public int QueuedTimestampCount
    {
        get
        {
            int total = 0;
            foreach (ChannelTracker tracker in _channelTrackers.Snapshot())
            {
                lock (tracker.Lock) total += tracker.Timestamps.Count;
            }
            return total;
        }
    }
}
