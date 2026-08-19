using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Core.Resilience;

/// <summary>
/// Packet-flood circuit breaker and UI resource clamp.
/// </summary>
/// <remarks>
/// Detects channels exceeding <see cref="MaxAllowedRatePerSec"/>, isolates them for
/// <see cref="IsolationDuration"/>, and signals the UI to subsample so the window stays responsive.
/// <para>
/// Rate accounting is amortised O(1): each channel keeps a timestamp queue that is pruned from the
/// front as it ages out, and the sliding-window count is the queue length. The previous version ran
/// <c>Timestamps.Count(t =&gt; t &gt;= windowStart)</c> — a full LINQ scan of up to
/// <see cref="MaxAllowedRatePerSec"/> entries per channel — from properties the UI reads every
/// frame, so the flood defence became most expensive exactly during a flood.
/// </para>
/// </remarks>
public class TelemetryCircuitBreaker : ICircuitBreaker
{
    private const long TicksPerSecond = 10_000_000;

    private sealed class ChannelTracker
    {
        public readonly object Lock = new();
        public readonly Queue<long> Timestamps = new();

        /// <summary>Drops entries older than the window. Amortised O(1) per recorded packet.</summary>
        public int PruneAndCount(long windowStartTicks)
        {
            while (Timestamps.Count > 0 && Timestamps.Peek() < windowStartTicks)
            {
                Timestamps.Dequeue();
            }
            return Timestamps.Count;
        }
    }

    private readonly ConcurrentDictionary<string, DateTime> _isolatedChannels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ChannelTracker> _channelTrackers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-channel packet rate above which a channel is isolated.</summary>
    public int MaxAllowedRatePerSec { get; set; } = 50_000;

    /// <summary>Aggregate rate above which the UI is asked to subsample.</summary>
    public int UiClampRatePerSec { get; set; } = 10_000;

    public int UiMaxFrameRateHz { get; set; } = 60;

    public TimeSpan IsolationDuration { get; set; } = TimeSpan.FromSeconds(1);

    public event EventHandler<string>? ChannelIsolated;
    public event EventHandler<string>? ChannelRestored;

    /// <summary>Aggregate packets observed across all channels in the last second.</summary>
    public int CurrentAggregateRate => CountRecentPackets();

    public bool IsUiResourceClamped => CountRecentPackets() > UiClampRatePerSec || !_isolatedChannels.IsEmpty;

    public int SubsampleRatio
    {
        get
        {
            int recent = CountRecentPackets();
            if (recent <= UiClampRatePerSec) return 1;

            return Math.Clamp(recent / Math.Max(1, UiClampRatePerSec), 1, 100);
        }
    }

    public bool AllowPacketProcessing(string channelId)
    {
        channelId = Normalize(channelId);

        if (_isolatedChannels.TryGetValue(channelId, out DateTime isolatedUntil))
        {
            if (DateTime.UtcNow < isolatedUntil) return false;

            if (_isolatedChannels.TryRemove(channelId, out _))
            {
                ChannelRestored?.Invoke(this, channelId);
            }
        }

        ChannelTracker tracker = _channelTrackers.GetOrAdd(channelId, _ => new ChannelTracker());
        long nowTicks = DateTime.UtcNow.Ticks;

        int recent;
        lock (tracker.Lock)
        {
            tracker.Timestamps.Enqueue(nowTicks);
            recent = tracker.PruneAndCount(nowTicks - TicksPerSecond);
        }

        if (recent > MaxAllowedRatePerSec)
        {
            _isolatedChannels[channelId] = DateTime.UtcNow.Add(IsolationDuration);
            ChannelIsolated?.Invoke(this, channelId);
            return false;
        }

        return true;
    }

    public void RecordPacket(string channelId)
    {
        channelId = Normalize(channelId);
        ChannelTracker tracker = _channelTrackers.GetOrAdd(channelId, _ => new ChannelTracker());
        long nowTicks = DateTime.UtcNow.Ticks;

        lock (tracker.Lock)
        {
            tracker.Timestamps.Enqueue(nowTicks);
            tracker.PruneAndCount(nowTicks - TicksPerSecond);
        }
    }

    public void ReportPacketRate(string channelId, int packetsPerSecond)
    {
        channelId = Normalize(channelId);

        if (packetsPerSecond > MaxAllowedRatePerSec)
        {
            _isolatedChannels[channelId] = DateTime.UtcNow.Add(IsolationDuration);
            ChannelIsolated?.Invoke(this, channelId);
        }
        else if (_isolatedChannels.TryRemove(channelId, out _))
        {
            ChannelRestored?.Invoke(this, channelId);
        }
    }

    public bool IsChannelIsolated(string channelId)
    {
        channelId = Normalize(channelId);

        if (_isolatedChannels.TryGetValue(channelId, out DateTime isolatedUntil))
        {
            if (DateTime.UtcNow < isolatedUntil) return true;

            if (_isolatedChannels.TryRemove(channelId, out _))
            {
                ChannelRestored?.Invoke(this, channelId);
            }
        }

        return false;
    }

    /// <summary>Clears all rate history and isolation state.</summary>
    public void Reset()
    {
        _channelTrackers.Clear();
        _isolatedChannels.Clear();
    }

    /// <summary>
    /// Sums the sliding-window counts across channels. Cost is proportional to the number of
    /// channels, not to the number of buffered packets.
    /// </summary>
    private int CountRecentPackets()
    {
        long windowStart = DateTime.UtcNow.Ticks - TicksPerSecond;
        int total = 0;

        foreach (ChannelTracker tracker in _channelTrackers.Values)
        {
            lock (tracker.Lock)
            {
                total += tracker.PruneAndCount(windowStart);
            }
        }

        return total;
    }

    private static string Normalize(string channelId) =>
        string.IsNullOrEmpty(channelId) ? "default" : channelId;
}
