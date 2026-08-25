using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Services;

/// <summary>
/// Multi-source time-sync jitter buffer with timestamp-sorted storage per node,
/// EMA clock drift alignment, linear interpolation between bounding samples, and pruning.
/// </summary>
public partial class TimeSyncJitterBuffer : ITimeSyncJitterBuffer
{
    private class NodeBuffer
    {
        public readonly object Lock = new();
        public readonly List<(double Timestamp, double Value)> Samples = new();

        /// <summary>Raw (masterTime - nodeTime) observations, oldest first.</summary>
        /// <remarks>
        /// Kept rather than smoothed away. The previous field was an EMA of these, which is a
        /// point estimate and discards the very thing ARCHITECTURE §3 asks for: the spread across
        /// observations is the only error bar available, and an EMA throws away the residuals it
        /// is computed from.
        /// </remarks>
        public readonly Queue<double> ClockObservations = new();

        public ClockOffsetEstimate Offset = ClockOffsetEstimate.Unmeasured;
    }

    private readonly ConcurrentDictionary<string, NodeBuffer> _buffers = new(StringComparer.OrdinalIgnoreCase);
    private readonly double _retentionWindowSec = 10.0;

    public void EnqueueSample(string nodeId, double timestamp, double value)
    {
        var nodeBuf = _buffers.GetOrAdd(nodeId, _ => new NodeBuffer());
        lock (nodeBuf.Lock)
        {
            // An unmeasured offset shifts by nothing, because nothing is known to shift by --
            // which is not the same as knowing the offset is zero. The estimate keeps those two
            // apart for anyone who asks; here they happen to produce the same arithmetic.
            double alignedTimestamp =
                timestamp + (nodeBuf.Offset.HasOffset ? nodeBuf.Offset.OffsetSec : 0.0);

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

    /// <summary>The node's value at <paramref name="masterTimestamp"/>, and how it was obtained.</summary>
    public AlignedSample GetAligned(string nodeId, double masterTimestamp)
    {
        // A node nobody has heard from is not a node reading zero. Returning 0.0 here made those
        // two indistinguishable, and a caller plotting the result drew a flat line through a gap.
        if (!_buffers.TryGetValue(nodeId, out var nodeBuf))
        {
            return AlignedSample.None;
        }

        lock (nodeBuf.Lock)
        {
            var samples = nodeBuf.Samples;
            if (samples.Count == 0)
            {
                return AlignedSample.None;
            }

            // Prune old samples prior to retention window
            double pruneThreshold = masterTimestamp - _retentionWindowSec;
            while (samples.Count > 2 && samples[0].Timestamp < pruneThreshold)
            {
                samples.RemoveAt(0);
            }

            // Outside the buffer the answer is the nearest sample, and it is labelled as held with
            // the size of the gap, so the caller can decide whether it is close enough to use. A
            // held value describes a different instant from the one asked about.
            if (masterTimestamp <= samples[0].Timestamp)
            {
                double gap = samples[0].Timestamp - masterTimestamp;
                return gap < 1e-9
                    ? new AlignedSample(samples[0].Value, AlignmentKind.Exact, 0.0)
                    : new AlignedSample(samples[0].Value, AlignmentKind.HeldBefore, gap);
            }

            if (masterTimestamp >= samples[^1].Timestamp)
            {
                double gap = masterTimestamp - samples[^1].Timestamp;
                return gap < 1e-9
                    ? new AlignedSample(samples[^1].Value, AlignmentKind.Exact, 0.0)
                    : new AlignedSample(samples[^1].Value, AlignmentKind.HeldAfter, gap);
            }

            // Binary search to locate bounding interval [t0, t1]
            int low = 0;
            int high = samples.Count - 1;

            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                if (samples[mid].Timestamp == masterTimestamp)
                {
                    return new AlignedSample(samples[mid].Value, AlignmentKind.Exact, 0.0);
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
                return new AlignedSample(v0, AlignmentKind.Exact, 0.0);
            }

            // Linear interpolation: v = v0 + (v1 - v0) * (t - t0) / (t1 - t0). Labelled as
            // interpolated rather than measured, because it is a value nothing reported.
            double fraction = (masterTimestamp - t0) / (t1 - t0);
            return new AlignedSample(v0 + (v1 - v0) * fraction, AlignmentKind.Interpolated, 0.0);
        }
    }

    public void ClearBuffer(string nodeId)
    {
        if (_buffers.TryGetValue(nodeId, out var nodeBuf))
        {
            lock (nodeBuf.Lock)
            {
                nodeBuf.Samples.Clear();
                nodeBuf.ClockObservations.Clear();
                nodeBuf.Offset = ClockOffsetEstimate.Unmeasured;
            }
        }
    }
}
