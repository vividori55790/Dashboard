using System;

namespace TelemetryDashboard.Core.Query;

/// <summary>
/// States exactly what a rendered series is: how many samples stand behind it, which reduction
/// produced it, how wide one bucket is, and what that reduction threw away.
/// </summary>
/// <remarks>
/// This type is the reason the query API exists in this shape. A consumer handed only points
/// cannot tell 2,000 samples apart from 2,000,000 samples reduced 1000:1, and will read a
/// downsampled line as though it were the signal. Every field here is measured from the samples
/// actually in the window; none is a nominal or configured value.
/// </remarks>
public sealed class ReductionMetadata
{
    /// <summary>Raw samples in the requested window, before any reduction.</summary>
    public required int SourceSampleCount { get; init; }

    /// <summary>Points actually returned.</summary>
    public required int ReturnedPointCount { get; init; }

    /// <summary>Which reduction ran. <see cref="ReductionMethod.None"/> means the points are raw.</summary>
    public required ReductionMethod Method { get; init; }

    /// <summary>
    /// True when no excursion in the window can be absent from the output.
    /// </summary>
    /// <remarks>
    /// Only <see cref="ReductionMethod.MinMax"/> and <see cref="ReductionMethod.None"/> earn this.
    /// A false here is a warning that the drawn line may be missing a spike that occurred.
    /// </remarks>
    public required bool PreservesExtremes { get; init; }

    /// <summary>Time width of one bucket, in seconds. Zero when nothing was reduced.</summary>
    public required double BucketWidthSec { get; init; }

    /// <summary>Timestamp of the first sample present in the window.</summary>
    public required double WindowStartSec { get; init; }

    /// <summary>Timestamp of the last sample present in the window.</summary>
    public required double WindowEndSec { get; init; }

    /// <summary>
    /// The true minimum over every raw sample in the window, reduction or not.
    /// </summary>
    /// <remarks>
    /// Carried so a consumer can check a shape-preserving reduction against the data: if the drawn
    /// line never reaches this value, the reduction dropped the excursion and the reader can see
    /// that it did instead of trusting the curve.
    /// </remarks>
    public required double SourceMinimum { get; init; }

    /// <summary>The true maximum over every raw sample in the window.</summary>
    public required double SourceMaximum { get; init; }

    /// <summary>
    /// True when retention, not the sensor, is why the window starts where it does.
    /// </summary>
    /// <remarks>
    /// Set when the oldest sample still held is newer than the requested start. Without it a
    /// buffer that only kept ten seconds of a sixty-second request renders as fifty seconds of
    /// silence, which reads as an outage that never happened.
    /// </remarks>
    public bool WindowTruncatedByRetention { get; init; }

    /// <summary>Samples that did not survive into the output.</summary>
    public int DiscardedSampleCount => Math.Max(0, SourceSampleCount - ReturnedPointCount);

    /// <summary>Samples in, per point out. 1.0 when the points are raw.</summary>
    public double CompressionRatio =>
        ReturnedPointCount <= 0 ? 0.0 : (double)SourceSampleCount / ReturnedPointCount;

    /// <summary>Plain statement of what this reduction removed, for display beside the chart.</summary>
    public string DiscardedDescription => Method switch
    {
        ReductionMethod.None =>
            "Nothing. Every sample in the window is present.",
        ReductionMethod.MinMax =>
            "Every value strictly between each bucket's minimum and maximum, the path taken " +
            "between them, and how many times the signal crossed any level inside the bucket. " +
            "Both extremes of every bucket survive, so no excursion is missing.",
        ReductionMethod.LargestTriangleThreeBuckets =>
            "All but one sample per bucket, chosen for visual shape. Extremes are NOT preserved: " +
            "a spike can be absent from these points. Compare SourceMinimum and SourceMaximum " +
            "against the drawn line before concluding the signal was quiet.",
        _ => "Unknown reduction."
    };

    /// <summary>Metadata for a window that was returned whole.</summary>
    public static ReductionMetadata Raw(int count, double startSec, double endSec, double min, double max) => new()
    {
        SourceSampleCount = count,
        ReturnedPointCount = count,
        Method = ReductionMethod.None,
        PreservesExtremes = true,
        BucketWidthSec = 0.0,
        WindowStartSec = startSec,
        WindowEndSec = endSec,
        SourceMinimum = min,
        SourceMaximum = max
    };
}
