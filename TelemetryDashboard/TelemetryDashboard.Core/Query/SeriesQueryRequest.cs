using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Query;

/// <summary>
/// A query shaped by the screen: which channels, which window, and how many points the caller can
/// actually draw.
/// </summary>
/// <remarks>
/// <see cref="MaxPoints"/> is the parameter that fixes the scale problem. A chart 1,000 pixels
/// wide can resolve about 2,000 points; sending it a million costs 220 MB/s per viewer and is then
/// thrown away by the browser. The caller states its budget, the server meets it, and the reply
/// says what it cost.
/// </remarks>
public sealed class SeriesQueryRequest
{
    public SeriesQueryRequest(
        IReadOnlyList<string> channels,
        double startSec,
        double endSec,
        int maxPoints,
        ReductionMethod method = ReductionMethod.MinMax)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (endSec < startSec) throw new ArgumentOutOfRangeException(nameof(endSec), "The window ends before it starts.");

        int floor = SeriesReducer.MinimumPointBudget(method);
        if (maxPoints < floor)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPoints),
                $"{method} needs a budget of at least {floor} points.");
        }

        Channels = channels;
        StartSec = startSec;
        EndSec = endSec;
        MaxPoints = maxPoints;
        Method = method;
    }

    /// <summary>Channels to return, in the order the caller listed them.</summary>
    public IReadOnlyList<string> Channels { get; }

    /// <summary>Inclusive start of the window, seconds since the Unix epoch.</summary>
    public double StartSec { get; }

    /// <summary>Inclusive end of the window, seconds since the Unix epoch.</summary>
    public double EndSec { get; }

    /// <summary>Points the caller can draw per channel. The reply never exceeds this.</summary>
    public int MaxPoints { get; }

    /// <summary>Reduction to apply per channel when the window holds more than the budget.</summary>
    public ReductionMethod Method { get; }

    /// <summary>Builds a request for the most recent <paramref name="windowSec"/> seconds.</summary>
    public static SeriesQueryRequest Recent(
        IReadOnlyList<string> channels,
        double windowSec,
        int maxPoints,
        double nowSec,
        ReductionMethod method = ReductionMethod.MinMax) =>
        new(channels, nowSec - windowSec, nowSec, maxPoints, method);
}
