using System;

namespace TelemetryDashboard.Core.Storage;

/// <summary>
/// One point of an answer, whether it is a sample or a summary of many.
/// </summary>
/// <remarks>
/// Raw and aggregated tiers share this shape so a caller can plot either without a second code
/// path, but they are never confused for one another: the tier is stated on the result, and a raw
/// point has <see cref="Count"/> 1 with <see cref="StartUtc"/> equal to <see cref="EndUtc"/>.
/// </remarks>
public sealed record TieredSeriesPoint(
    DateTime StartUtc,
    DateTime EndUtc,
    long Count,
    double Min,
    double Max,
    double Sum,
    double M2)
{
    /// <summary>Mean of the samples behind this point.</summary>
    /// <remarks>
    /// NaN for a raw point that recorded no reading. It stays NaN rather than becoming zero, all
    /// the way to the caller, because zero is a temperature and "the sensor said nothing" is not.
    /// </remarks>
    public double Mean => Sum / Count;

    /// <summary>Standard deviation across the samples behind this point.</summary>
    public double PopulationStandardDeviation => Math.Sqrt(M2 / Count);

    /// <summary>Bessel-corrected standard deviation, or NaN when one sample stands behind the point.</summary>
    public double SampleStandardDeviation =>
        Count > 1 ? Math.Sqrt(M2 / (Count - 1)) : double.NaN;

    /// <summary>Whether this point is a single measurement rather than a summary.</summary>
    public bool IsSingleSample => Count == 1;

    /// <summary>Wraps one recorded sample, preserving a NaN reading as recorded.</summary>
    public static TieredSeriesPoint FromSample(DateTime timestampUtc, double value) =>
        new(timestampUtc, timestampUtc, 1, value, value, value, 0.0);

    /// <summary>Projects a stored rollup window onto the answer shape.</summary>
    public static TieredSeriesPoint FromWindow(RollupWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new TieredSeriesPoint(
            window.StartUtc, window.EndUtc, window.Count, window.Min, window.Max, window.Sum, window.M2);
    }
}
