using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Query;

/// <summary>
/// The answer to a <see cref="SeriesQueryRequest"/>: one series per requested channel, each
/// carrying its own account of what it is.
/// </summary>
public sealed class SeriesQueryResult
{
    public SeriesQueryResult(SeriesQueryRequest request, IReadOnlyList<ReducedSeries> series)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Series = series ?? throw new ArgumentNullException(nameof(series));

        long samples = 0;
        long points = 0;
        bool anyLossy = false;
        foreach (ReducedSeries entry in series)
        {
            samples += entry.Metadata.SourceSampleCount;
            points += entry.Metadata.ReturnedPointCount;
            if (entry.Metadata.Method != ReductionMethod.None) anyLossy = true;
        }

        SourceSampleCount = samples;
        ReturnedPointCount = points;
        IsReduced = anyLossy;
    }

    public SeriesQueryRequest Request { get; }

    /// <summary>One entry per requested channel, including channels that had no samples.</summary>
    public IReadOnlyList<ReducedSeries> Series { get; }

    /// <summary>Raw samples behind the whole reply, summed across channels.</summary>
    public long SourceSampleCount { get; }

    /// <summary>Points in the whole reply, summed across channels.</summary>
    public long ReturnedPointCount { get; }

    /// <summary>True when at least one series was reduced and is therefore not raw data.</summary>
    public bool IsReduced { get; }

    /// <summary>Samples in, per point out, across the whole reply.</summary>
    public double CompressionRatio =>
        ReturnedPointCount <= 0 ? 0.0 : (double)SourceSampleCount / ReturnedPointCount;
}
