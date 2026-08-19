using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Core.Services;

/// <summary>
/// Multi-source time-sync jitter buffer with timestamp-sorted storage per node,
/// EMA clock drift alignment, linear interpolation between bounding samples, and pruning.
/// </summary>
public class TimeSyncJitterBuffer : ITimeSyncJitterBuffer
{
    private class NodeBuffer
    {
        public readonly object Lock = new();
        public readonly List<(double Timestamp, double Value)> Samples = new();
        public double ClockOffset; // masterTime - nodeTime
        public double DriftAlpha = 0.1; // EMA smoothing factor
    }

    private readonly ConcurrentDictionary<string, NodeBuffer> _buffers = new(StringComparer.OrdinalIgnoreCase);
    private readonly double _retentionWindowSec = 10.0;

    public void SyncNodeClock(string nodeId, double masterTime, double nodeTime)
    {
        var nodeBuf = _buffers.GetOrAdd(nodeId, _ => new NodeBuffer());
        lock (nodeBuf.Lock)
        {
            double measuredOffset = masterTime - nodeTime;
            if (nodeBuf.Samples.Count == 0 && nodeBuf.ClockOffset == 0.0)
            {
                nodeBuf.ClockOffset = measuredOffset;
            }
            else
            {
                nodeBuf.ClockOffset += nodeBuf.DriftAlpha * (measuredOffset - nodeBuf.ClockOffset);
            }
        }
    }

    public double GetClockOffset(string nodeId)
    {
        if (_buffers.TryGetValue(nodeId, out var nodeBuf))
        {
            lock (nodeBuf.Lock)
            {
                return nodeBuf.ClockOffset;
            }
        }
        return 0.0;
    }

    public void EnqueueSample(string nodeId, double timestamp, double value)
    {
        var nodeBuf = _buffers.GetOrAdd(nodeId, _ => new NodeBuffer());
        lock (nodeBuf.Lock)
        {
            double alignedTimestamp = timestamp + nodeBuf.ClockOffset;

            // Binary search insertion to keep samples sorted by timestamp
            int idx = nodeBuf.Samples.BinarySearch((alignedTimestamp, value), Comparer<(double Timestamp, double Value)>.Create((a, b) => a.Timestamp.CompareTo(b.Timestamp)));
            if (idx < 0)
            {
                idx = ~idx;
            }
            nodeBuf.Samples.Insert(idx, (alignedTimestamp, value));

            // Bound maximum samples to 1000 per node
            while (nodeBuf.Samples.Count > 1000)
            {
                nodeBuf.Samples.RemoveAt(0);
            }
        }
    }

    public double GetAlignedSample(string nodeId, double masterTimestamp)
    {
        if (!_buffers.TryGetValue(nodeId, out var nodeBuf))
        {
            return 0.0;
        }

        lock (nodeBuf.Lock)
        {
            var samples = nodeBuf.Samples;
            if (samples.Count == 0)
            {
                return 0.0;
            }

            // Prune old samples prior to retention window
            double pruneThreshold = masterTimestamp - _retentionWindowSec;
            while (samples.Count > 2 && samples[0].Timestamp < pruneThreshold)
            {
                samples.RemoveAt(0);
            }

            if (samples.Count == 1 || masterTimestamp <= samples[0].Timestamp)
            {
                return samples[0].Value;
            }

            if (masterTimestamp >= samples[^1].Timestamp)
            {
                return samples[^1].Value;
            }

            // Binary search to locate bounding interval [t0, t1]
            int low = 0;
            int high = samples.Count - 1;

            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                if (samples[mid].Timestamp == masterTimestamp)
                {
                    return samples[mid].Value;
                }
                if (samples[mid].Timestamp < masterTimestamp)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            // high is index of lower bound sample (t0), low is upper bound sample (t1)
            int i0 = Math.Max(0, high);
            int i1 = Math.Min(samples.Count - 1, low);

            var (t0, v0) = samples[i0];
            var (t1, v1) = samples[i1];

            if (Math.Abs(t1 - t0) < 1e-9)
            {
                return v0;
            }

            // Linear interpolation: v = v0 + (v1 - v0) * (t - t0) / (t1 - t0)
            double fraction = (masterTimestamp - t0) / (t1 - t0);
            return v0 + (v1 - v0) * fraction;
        }
    }

    public void ClearBuffer(string nodeId)
    {
        if (_buffers.TryGetValue(nodeId, out var nodeBuf))
        {
            lock (nodeBuf.Lock)
            {
                nodeBuf.Samples.Clear();
                nodeBuf.ClockOffset = 0.0;
            }
        }
    }
}
