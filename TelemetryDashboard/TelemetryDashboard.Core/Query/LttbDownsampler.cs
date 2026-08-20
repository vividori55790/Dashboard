using System;

namespace TelemetryDashboard.Core.Query;

/// <summary>
/// Largest-Triangle-Three-Buckets: keeps the sample from each bucket that contributes most to the
/// visible shape of the curve.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it guarantees.</b> Every emitted point is a real sample at its real timestamp; the
/// first and last samples of the window are always emitted; the output length is exactly the
/// budget. Nothing is averaged into the result — the mean of the following bucket is computed to
/// score candidates and is then thrown away, never emitted.
/// </para>
/// <para>
/// <b>What it discards, and this matters.</b> LTTB does <em>not</em> preserve extremes. A spike
/// narrower than a bucket is kept only when it happens to win its bucket's triangle-area contest,
/// and a spike that shares its bucket with a larger deflection is silently dropped. It is the
/// right reduction when the question is "what shape is this signal", and the wrong one when the
/// question is "did anything excursion". Use <see cref="MinMaxDownsampler"/> for the second.
/// A caller cannot tell the two apart from the points alone, which is why every result reduced
/// this way is labelled with <c>preservesExtremes: false</c> and carries the true minimum and
/// maximum of the window alongside.
/// </para>
/// <para>Pure and allocation-free: the caller owns both spans.</para>
/// </remarks>
public static class LttbDownsampler
{
    /// <summary>Smallest budget the algorithm is defined for: first, one chosen, last.</summary>
    public const int MinimumPointBudget = 3;

    /// <summary>
    /// Writes the reduction of <paramref name="source"/> into <paramref name="destination"/>.
    /// </summary>
    /// <param name="source">Samples ordered by ascending timestamp.</param>
    /// <param name="maxPoints">Exact output length once a reduction is needed.</param>
    /// <param name="destination">Buffer of at least <paramref name="maxPoints"/> points.</param>
    /// <returns>Points written to <paramref name="destination"/>.</returns>
    public static int Reduce(ReadOnlySpan<SeriesPoint> source, int maxPoints, Span<SeriesPoint> destination)
    {
        if (maxPoints < MinimumPointBudget)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPoints),
                $"Largest-Triangle-Three-Buckets needs at least {MinimumPointBudget} points.");
        }

        if (source.Length <= maxPoints)
        {
            source.CopyTo(destination);
            return source.Length;
        }

        double bucketSize = (double)(source.Length - 2) / (maxPoints - 2);

        destination[0] = source[0];
        int selected = 0;
        int written = 1;

        for (int bucket = 0; bucket < maxPoints - 2; bucket++)
        {
            int start = (int)(bucket * bucketSize) + 1;
            int end = Math.Min((int)((bucket + 1) * bucketSize) + 1, source.Length - 1);

            (double avgTime, double avgValue) = NextBucketCentroid(source, bucket, bucketSize);

            int best = start;
            double bestArea = -1.0;
            SeriesPoint anchor = source[selected];

            for (int i = start; i < end; i++)
            {
                // Twice the triangle area spanned by the previously kept point, this candidate and
                // the centroid of the next bucket. The factor of two is common to every candidate,
                // so it is left in rather than paid for per point.
                double area = Math.Abs(
                    (anchor.TimestampSec - avgTime) * (source[i].Value - anchor.Value) -
                    (anchor.TimestampSec - source[i].TimestampSec) * (avgValue - anchor.Value));

                if (area > bestArea)
                {
                    bestArea = area;
                    best = i;
                }
            }

            destination[written++] = source[best];
            selected = best;
        }

        destination[written++] = source[^1];
        return written;
    }

    /// <summary>
    /// Mean position of the next bucket, used only to score candidates.
    /// </summary>
    /// <remarks>
    /// This value is the one number in the algorithm that no sensor produced. It never reaches
    /// the output: it exists to answer "which real sample best represents this bucket", and is
    /// discarded the moment that question is answered.
    /// </remarks>
    private static (double Time, double Value) NextBucketCentroid(
        ReadOnlySpan<SeriesPoint> source, int bucket, double bucketSize)
    {
        int start = (int)((bucket + 1) * bucketSize) + 1;
        int end = Math.Min((int)((bucket + 2) * bucketSize) + 1, source.Length);

        if (start >= end)
        {
            SeriesPoint last = source[^1];
            return (last.TimestampSec, last.Value);
        }

        double time = 0.0;
        double value = 0.0;
        for (int i = start; i < end; i++)
        {
            time += source[i].TimestampSec;
            value += source[i].Value;
        }

        int count = end - start;
        return (time / count, value / count);
    }
}
