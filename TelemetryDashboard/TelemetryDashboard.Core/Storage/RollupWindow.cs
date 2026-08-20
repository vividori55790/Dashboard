using System;

namespace TelemetryDashboard.Core.Storage;

/// <summary>
/// One completed aggregation bucket: what a channel did over one interval.
/// </summary>
/// <remarks>
/// A window cannot be constructed with a zero count. That is the central rule of this store made
/// structural rather than conventional: an interval in which no sensor spoke has no window, and a
/// caller reading the series sees the interval missing instead of seeing a mean of zero. Every
/// path that could produce an empty bucket — an all-NaN minute, a channel that went quiet, a gap
/// between two recordings — therefore ends by writing nothing at all.
/// </remarks>
public sealed record RollupWindow
{
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is not positive.</exception>
    public RollupWindow(
        ChannelKey channel,
        RollupInterval interval,
        long bucketStartUtcTicks,
        long count,
        double min,
        double max,
        double sum,
        double m2)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count), count,
                "A rollup window must contain at least one measurement; an empty interval is stored as no window.");
        }

        Channel = channel;
        Interval = interval;
        BucketStartUtcTicks = bucketStartUtcTicks;
        Count = count;
        Min = min;
        Max = max;
        Sum = sum;
        M2 = m2;
    }

    /// <summary>Series this window summarises.</summary>
    public ChannelKey Channel { get; }

    /// <summary>Width of the window.</summary>
    public RollupInterval Interval { get; }

    /// <summary>Aligned start of the window, in UTC ticks.</summary>
    public long BucketStartUtcTicks { get; }

    /// <summary>Measurements folded in. Always positive.</summary>
    public long Count { get; }

    /// <summary>Smallest measurement in the window.</summary>
    public double Min { get; }

    /// <summary>Largest measurement in the window.</summary>
    public double Max { get; }

    /// <summary>Sum of the measurements, kept so windows stay mergeable.</summary>
    public double Sum { get; }

    /// <summary>Sum of squared deviations, kept so standard deviation is derivable and mergeable.</summary>
    public double M2 { get; }

    /// <summary>Inclusive start of the window.</summary>
    public DateTime StartUtc => new(BucketStartUtcTicks, DateTimeKind.Utc);

    /// <summary>Exclusive end of the window.</summary>
    public DateTime EndUtc => new(BucketStartUtcTicks + Interval.TicksPer(), DateTimeKind.Utc);

    /// <summary>Arithmetic mean of the measurements.</summary>
    public double Mean => Sum / Count;

    /// <summary>Standard deviation across the window's own samples.</summary>
    public double PopulationStandardDeviation => Math.Sqrt(M2 / Count);

    /// <summary>Bessel-corrected standard deviation, or NaN when the window holds one sample.</summary>
    public double SampleStandardDeviation =>
        Count > 1 ? Math.Sqrt(M2 / (Count - 1)) : double.NaN;

    /// <summary>Builds a window from a filled accumulator.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The accumulator holds no measurement.</exception>
    public static RollupWindow From(
        ChannelKey channel, RollupInterval interval, long bucketStartUtcTicks, RollupAccumulator accumulator)
    {
        ArgumentNullException.ThrowIfNull(accumulator);
        return new RollupWindow(
            channel, interval, bucketStartUtcTicks,
            accumulator.Count, accumulator.Min, accumulator.Max, accumulator.Sum, accumulator.M2);
    }
}
