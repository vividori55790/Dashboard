using System;

namespace TelemetryDashboard.Core.Query;

/// <summary>
/// Reduces a time-ordered series to at most N points by emitting the minimum and the maximum of
/// every equal-width time bucket.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it guarantees.</b> For every bucket, the sample with the lowest value and the sample
/// with the highest value are both emitted, unchanged, at their own timestamps. The global
/// minimum and the global maximum of the window are therefore always present in the output: a
/// single-sample spike cannot be removed by this reduction, whatever the ratio. That is the whole
/// reason it exists. Plain decimation — take every Nth sample — deletes exactly the sample an
/// operator is looking for and leaves a chart that looks calm.
/// </para>
/// <para>
/// <b>What it discards.</b> Everything strictly between a bucket's minimum and its maximum: the
/// path the signal took inside the bucket, the number of times it crossed a level, and the
/// ordering of the samples that were not extremes. A rendered pair of points is an envelope for
/// its bucket, not a waveform.
/// </para>
/// <para>
/// Buckets are equal <em>time</em> width, not equal sample count, so one bucket is one pixel
/// column on a chart of the same width and <see cref="BucketWidthSec"/> is a fact about the
/// output rather than an average. A bucket that contains no samples emits nothing; it is never
/// filled in with a neighbour's value or an interpolation.
/// </para>
/// <para>
/// Pure and allocation-free: the caller owns both spans and no state is kept between calls.
/// </para>
/// </remarks>
public static class MinMaxDownsampler
{
    /// <summary>Smallest output budget the reduction can honour, one bucket of two extremes.</summary>
    public const int MinimumPointBudget = 2;

    /// <summary>Time width of one bucket for the given data extent and point budget.</summary>
    public static double BucketWidthSec(double firstSec, double lastSec, int maxPoints)
    {
        int buckets = BucketCount(maxPoints);
        double span = lastSec - firstSec;
        return buckets <= 0 || span <= 0 ? 0.0 : span / buckets;
    }

    /// <summary>Number of buckets a budget of <paramref name="maxPoints"/> pays for.</summary>
    public static int BucketCount(int maxPoints) => maxPoints / 2;

    /// <summary>
    /// Writes the reduction of <paramref name="source"/> into <paramref name="destination"/>.
    /// </summary>
    /// <param name="source">Samples ordered by ascending timestamp.</param>
    /// <param name="maxPoints">Hard ceiling on emitted points. Never exceeded.</param>
    /// <param name="destination">
    /// Buffer of at least <paramref name="maxPoints"/> points, or of <paramref name="source"/>'s
    /// length when it is shorter.
    /// </param>
    /// <returns>Points written to <paramref name="destination"/>.</returns>
    public static int Reduce(ReadOnlySpan<SeriesPoint> source, int maxPoints, Span<SeriesPoint> destination)
    {
        if (maxPoints < MinimumPointBudget)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPoints),
                $"A min/max reduction needs at least {MinimumPointBudget} points; one bucket emits two.");
        }

        if (source.Length <= maxPoints)
        {
            // Nothing to reduce. Copying verbatim is the only answer that does not claim a
            // reduction happened; the caller reports ReductionMethod.None for this case.
            source.CopyTo(destination);
            return source.Length;
        }

        int buckets = BucketCount(maxPoints);
        double first = source[0].TimestampSec;
        double width = BucketWidthSec(first, source[^1].TimestampSec, maxPoints);

        // A window with no time span at all (every sample stamped identically) has no buckets to
        // divide it into. Falling back to one bucket keeps the extremes, which is the guarantee.
        if (width <= 0.0) return EmitBucket(source, destination);

        int written = 0;
        int bucketStart = 0;
        int currentBucket = 0;

        for (int i = 1; i <= source.Length; i++)
        {
            int bucket = i == source.Length
                ? buckets
                : Math.Min(buckets - 1, (int)((source[i].TimestampSec - first) / width));

            if (bucket == currentBucket) continue;

            written += EmitBucket(source[bucketStart..i], destination[written..]);
            bucketStart = i;
            currentBucket = bucket;
        }

        return written;
    }

    /// <summary>
    /// Emits one bucket's extremes in chronological order, so the polyline never runs backwards.
    /// </summary>
    /// <remarks>
    /// When one sample is both the minimum and the maximum — a bucket of one, or a flat bucket —
    /// it is emitted once. Duplicating it would inflate the point count with a value the data
    /// never produced twice.
    /// </remarks>
    private static int EmitBucket(ReadOnlySpan<SeriesPoint> bucket, Span<SeriesPoint> destination)
    {
        if (bucket.IsEmpty || destination.IsEmpty) return 0;

        int minIndex = 0;
        int maxIndex = 0;
        for (int i = 1; i < bucket.Length; i++)
        {
            if (bucket[i].Value < bucket[minIndex].Value) minIndex = i;
            if (bucket[i].Value > bucket[maxIndex].Value) maxIndex = i;
        }

        if (minIndex == maxIndex)
        {
            destination[0] = bucket[minIndex];
            return 1;
        }

        int earlier = Math.Min(minIndex, maxIndex);
        int later = Math.Max(minIndex, maxIndex);

        // The budget is a ceiling, not a target: if only one slot is left, the extreme that would
        // be dropped is the later one, and the caller's ceiling still holds.
        destination[0] = bucket[earlier];
        if (destination.Length < 2) return 1;

        destination[1] = bucket[later];
        return 2;
    }
}
