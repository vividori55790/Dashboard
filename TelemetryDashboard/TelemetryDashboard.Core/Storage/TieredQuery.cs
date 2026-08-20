using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Storage;

/// <summary>
/// A time-window question: one channel, one span, and how fine an answer the caller can use.
/// </summary>
/// <remarks>
/// <see cref="Resolution"/> is a request, not a demand — the store answers from the coarsest tier
/// that still meets it, and says which one that was. Leaving it null asks for raw samples.
/// </remarks>
public sealed record TieredQueryRequest(
    ChannelKey Channel,
    DateTime StartUtc,
    DateTime EndUtc,
    TimeSpan? Resolution = null,
    int MaxPoints = 5_000)
{
    /// <summary>Validates the window, treating Unspecified timestamps as UTC.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The window ends before it starts, or MaxPoints is not positive.</exception>
    public TieredQueryRequest Validated()
    {
        if (RollupIntervals.ToUtcTicks(EndUtc) < RollupIntervals.ToUtcTicks(StartUtc))
        {
            throw new ArgumentOutOfRangeException(
                nameof(EndUtc), EndUtc, "A query window must end at or after it starts.");
        }

        if (MaxPoints <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxPoints), MaxPoints, "MaxPoints must be positive; a query that can return nothing is a bug.");
        }

        return this;
    }
}

/// <summary>
/// An answer, labelled with the tier it came from.
/// </summary>
/// <remarks>
/// <see cref="Tier"/> and <see cref="TierReason"/> are the parts that stop a coarse answer being
/// read as a fine one. A caller that plots <see cref="Points"/> without looking at them will still
/// see hourly means drawn as hourly means, because each point carries its own start and end.
/// </remarks>
public sealed record TieredQueryResult(
    TelemetryTier Tier,
    TimeSpan Resolution,
    string TierReason,
    IReadOnlyList<TieredSeriesPoint> Points,
    bool Truncated)
{
    /// <summary>Whether these points are individual samples rather than summaries.</summary>
    public bool IsRaw => Tier == TelemetryTier.Raw;

    /// <summary>Measurements standing behind the answer, across every point.</summary>
    public long SampleCount
    {
        get
        {
            long total = 0;
            foreach (TieredSeriesPoint point in Points) total += point.Count;
            return total;
        }
    }

    /// <summary>One line naming the tier, for logs and for a chart subtitle.</summary>
    public string Describe() =>
        $"{Points.Count} point(s) from the {Tier} tier" +
        (IsRaw ? string.Empty : $" ({Resolution.TotalSeconds:N0} s per point)") +
        $", {SampleCount} sample(s) behind them: {TierReason}" +
        (Truncated ? " [truncated at MaxPoints]" : string.Empty);
}
