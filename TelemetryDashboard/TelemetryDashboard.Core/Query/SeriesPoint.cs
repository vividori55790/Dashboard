using System;

namespace TelemetryDashboard.Core.Query;

/// <summary>
/// One measured sample: the instant it was taken and the value that was measured.
/// </summary>
/// <remarks>
/// Every point that leaves a reduction in this namespace is a copy of one of these that arrived
/// from a sensor. No stage constructs a <see cref="SeriesPoint"/> from an average, an
/// interpolation or a bucket centre, so a timestamp on screen is always a timestamp something was
/// actually measured at.
/// </remarks>
/// <param name="TimestampSec">Seconds since the Unix epoch, UTC.</param>
/// <param name="Value">The measured value, exactly as recorded.</param>
public readonly record struct SeriesPoint(double TimestampSec, double Value);

/// <summary>How a series was reduced to fit the number of points a caller can draw.</summary>
public enum ReductionMethod
{
    /// <summary>Nothing was discarded: every sample in the window is present.</summary>
    None = 0,

    /// <summary>
    /// Minimum and maximum of each time bucket, both as real samples at their real timestamps.
    /// </summary>
    MinMax = 1,

    /// <summary>
    /// Largest-Triangle-Three-Buckets: one real sample per bucket, chosen for visual shape.
    /// Does not preserve extremes.
    /// </summary>
    LargestTriangleThreeBuckets = 2
}
