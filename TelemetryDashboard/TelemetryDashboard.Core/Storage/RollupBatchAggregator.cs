using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Storage;

/// <summary>Bucket a sample belongs to: a channel, a tier and an aligned start.</summary>
public readonly record struct RollupBucketKey(
    ChannelKey Channel, RollupInterval Interval, long BucketStartUtcTicks);

/// <summary>
/// Folds one batch of samples into partial windows, one per tier per bucket the batch touches.
/// </summary>
/// <remarks>
/// Each sample is aggregated into all three tiers directly, rather than cascading second-buckets
/// into minute-buckets and those into hour-buckets. The two give identical numbers — the merge in
/// <see cref="RollupAccumulator"/> is exact — but cascading makes the hour tier depend on the
/// second tier still being present, so pruning the fine tier would then damage the coarse one. Here
/// each tier stands alone, and the cost is bounded by buckets touched rather than samples seen: a
/// thousand samples of one channel inside one second produce three partial windows, not three
/// thousand.
/// <para>
/// The output is a set of partial windows to be <em>merged</em> into whatever the store already
/// holds for those buckets, never to overwrite it. That is what makes the rollups incremental: a
/// batch is summarised once, on arrival, and no raw sample is ever read a second time.
/// </para>
/// </remarks>
public sealed class RollupBatchAggregator
{
    private readonly Dictionary<RollupBucketKey, RollupAccumulator> _buckets = new();

    /// <summary>Samples folded in.</summary>
    public long AcceptedCount { get; private set; }

    /// <summary>Samples discarded as NaN — readings that were not readings.</summary>
    public long NoReadingCount { get; private set; }

    /// <summary>Distinct buckets this batch touched, across all tiers.</summary>
    public int BucketCount => _buckets.Count;

    /// <summary>
    /// Folds one sample into every tier. Returns false for NaN, which is counted and dropped.
    /// </summary>
    /// <remarks>
    /// <paramref name="timestamp"/> is interpreted by <see cref="RollupIntervals.ToUtcTicks"/>:
    /// Unspecified means UTC, not local.
    /// </remarks>
    public bool Add(ChannelKey channel, DateTime timestamp, double value)
    {
        if (double.IsNaN(value))
        {
            NoReadingCount++;
            return false;
        }

        long ticks = RollupIntervals.ToUtcTicks(timestamp);
        foreach (RollupInterval interval in RollupIntervals.All)
        {
            var key = new RollupBucketKey(channel, interval, interval.BucketStartTicks(ticks));
            if (!_buckets.TryGetValue(key, out RollupAccumulator? accumulator))
            {
                accumulator = new RollupAccumulator();
                _buckets[key] = accumulator;
            }

            accumulator.Add(value);
        }

        AcceptedCount++;
        return true;
    }

    /// <summary>
    /// The partial windows this batch produced. Empty when every sample was NaN.
    /// </summary>
    public IReadOnlyList<RollupWindow> Windows()
    {
        var windows = new List<RollupWindow>(_buckets.Count);
        foreach ((RollupBucketKey key, RollupAccumulator accumulator) in _buckets)
        {
            // Cannot currently be false — Add filters NaN before touching a bucket — but the check
            // is what guarantees an empty bucket can never reach storage as a zeroed window.
            if (accumulator.HasMeasurement)
            {
                windows.Add(RollupWindow.From(
                    key.Channel, key.Interval, key.BucketStartUtcTicks, accumulator));
            }
        }

        return windows;
    }

    /// <summary>Empties the aggregator for reuse across batches.</summary>
    public void Clear()
    {
        _buckets.Clear();
        AcceptedCount = 0;
        NoReadingCount = 0;
    }
}
