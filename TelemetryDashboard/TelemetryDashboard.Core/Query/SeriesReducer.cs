using System;
using System.Buffers;

namespace TelemetryDashboard.Core.Query;

/// <summary>
/// Applies a reduction to one window of samples and describes the result honestly.
/// </summary>
/// <remarks>
/// One allocation per call — the returned point array — plus a pooled scratch buffer that is
/// returned before the method exits. The true minimum and maximum are computed over the raw
/// window in the same pass that copies it, so the metadata can state the real extremes even when
/// the reduction chosen does not preserve them.
/// </remarks>
public static class SeriesReducer
{
    /// <summary>Reduces <paramref name="window"/> to at most <paramref name="maxPoints"/> points.</summary>
    /// <param name="channel">Channel identifier, copied into the result.</param>
    /// <param name="window">Samples ordered by ascending timestamp.</param>
    /// <param name="maxPoints">Ceiling on the returned point count. Never exceeded.</param>
    /// <param name="method">Reduction to apply when the window does not already fit.</param>
    public static ReducedSeries Reduce(
        string channel,
        ReadOnlySpan<SeriesPoint> window,
        int maxPoints,
        ReductionMethod method)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (maxPoints <= 0) throw new ArgumentOutOfRangeException(nameof(maxPoints), "A caller must be able to draw at least one point.");
        if (window.IsEmpty) return ReducedSeries.Empty(channel);

        (double min, double max) = Extremes(window);
        double startSec = window[0].TimestampSec;
        double endSec = window[^1].TimestampSec;

        if (window.Length <= maxPoints || method == ReductionMethod.None)
        {
            // Below the budget nothing is discarded, so nothing may be claimed to have been. Above
            // it with ReductionMethod.None the window is truncated rather than reduced, and the
            // metadata says so by reporting the count that survived against the count that existed.
            int keep = Math.Min(window.Length, maxPoints);
            SeriesPoint[] verbatim = window[..keep].ToArray();
            return new ReducedSeries(channel, verbatim, new ReductionMetadata
            {
                SourceSampleCount = window.Length,
                ReturnedPointCount = keep,
                Method = ReductionMethod.None,
                PreservesExtremes = keep == window.Length,
                BucketWidthSec = 0.0,
                WindowStartSec = startSec,
                WindowEndSec = endSec,
                SourceMinimum = min,
                SourceMaximum = max
            });
        }

        SeriesPoint[] scratch = ArrayPool<SeriesPoint>.Shared.Rent(maxPoints);
        try
        {
            int written = method == ReductionMethod.MinMax
                ? MinMaxDownsampler.Reduce(window, maxPoints, scratch)
                : LttbDownsampler.Reduce(window, maxPoints, scratch);

            var points = new SeriesPoint[written];
            Array.Copy(scratch, points, written);

            return new ReducedSeries(channel, points, new ReductionMetadata
            {
                SourceSampleCount = window.Length,
                ReturnedPointCount = written,
                Method = method,
                PreservesExtremes = method == ReductionMethod.MinMax,
                BucketWidthSec = BucketWidthSec(method, startSec, endSec, maxPoints, written),
                WindowStartSec = startSec,
                WindowEndSec = endSec,
                SourceMinimum = min,
                SourceMaximum = max
            });
        }
        finally
        {
            ArrayPool<SeriesPoint>.Shared.Return(scratch);
        }
    }

    /// <summary>Smallest budget the given method can honour.</summary>
    public static int MinimumPointBudget(ReductionMethod method) => method switch
    {
        ReductionMethod.MinMax => MinMaxDownsampler.MinimumPointBudget,
        ReductionMethod.LargestTriangleThreeBuckets => LttbDownsampler.MinimumPointBudget,
        _ => 1
    };

    private static double BucketWidthSec(
        ReductionMethod method, double startSec, double endSec, int maxPoints, int written)
    {
        double span = endSec - startSec;
        if (span <= 0.0) return 0.0;

        return method switch
        {
            ReductionMethod.MinMax => MinMaxDownsampler.BucketWidthSec(startSec, endSec, maxPoints),
            ReductionMethod.LargestTriangleThreeBuckets => written <= 2 ? span : span / (written - 2),
            _ => 0.0
        };
    }

    private static (double Min, double Max) Extremes(ReadOnlySpan<SeriesPoint> window)
    {
        double min = window[0].Value;
        double max = window[0].Value;
        for (int i = 1; i < window.Length; i++)
        {
            double value = window[i].Value;
            if (value < min) min = value;
            if (value > max) max = value;
        }
        return (min, max);
    }
}
