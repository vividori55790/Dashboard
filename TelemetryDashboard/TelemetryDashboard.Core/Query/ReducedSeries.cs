using System;

namespace TelemetryDashboard.Core.Query;

/// <summary>One channel's points together with the statement of what they are.</summary>
/// <remarks>
/// The points and the metadata travel as one object on purpose. Every path that has handed a
/// consumer bare points has ended with the consumer treating a reduction as raw data.
/// </remarks>
public sealed class ReducedSeries
{
    public ReducedSeries(string channel, SeriesPoint[] points, ReductionMetadata metadata)
    {
        Channel = channel ?? throw new ArgumentNullException(nameof(channel));
        Points = points ?? throw new ArgumentNullException(nameof(points));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <summary>Channel identifier, as the producer named it.</summary>
    public string Channel { get; }

    /// <summary>The points to draw. Every one is a real sample at its own timestamp.</summary>
    public SeriesPoint[] Points { get; }

    /// <summary>What these points are, and what was discarded to produce them.</summary>
    public ReductionMetadata Metadata { get; }

    /// <summary>A channel the caller asked for that has no samples in the window.</summary>
    /// <remarks>
    /// Returned rather than omitted so a silent sensor is visible as silent. A channel dropped
    /// from the response reads to a dashboard exactly like a channel it forgot to request.
    /// </remarks>
    public static ReducedSeries Empty(string channel) => new(
        channel,
        Array.Empty<SeriesPoint>(),
        new ReductionMetadata
        {
            SourceSampleCount = 0,
            ReturnedPointCount = 0,
            Method = ReductionMethod.None,
            PreservesExtremes = true,
            BucketWidthSec = 0.0,
            WindowStartSec = 0.0,
            WindowEndSec = 0.0,
            SourceMinimum = double.NaN,
            SourceMaximum = double.NaN
        });
}
