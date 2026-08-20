using System;

namespace TelemetryDashboard.Core.Storage;

/// <summary>Which tier should answer a query, and why.</summary>
/// <remarks>
/// Two forces pick the tier. The caller's requested resolution sets a ceiling on bucket width — an
/// answer coarser than what was asked for is the wrong answer. Everything else pushes coarser:
/// reading a month of 1 kHz samples to draw a thousand-pixel chart costs billions of rows to
/// produce a picture indistinguishable from the hourly one, and once raw data has been pruned the
/// fine tiers are not merely expensive but absent.
/// </remarks>
public static class TierSelector
{
    /// <summary>
    /// Picks the coarsest tier that still satisfies <paramref name="request"/>.
    /// </summary>
    /// <param name="request">The window and the resolution asked for.</param>
    /// <param name="rawAvailableFromUtc">
    /// Oldest raw sample the store still holds, or null when it holds none. A request reaching
    /// further back than this cannot be answered from raw data, however fine a resolution it asked
    /// for — the samples are gone.
    /// </param>
    public static (TelemetryTier Tier, string Reason) Select(
        TieredQueryRequest request, DateTime? rawAvailableFromUtc)
    {
        ArgumentNullException.ThrowIfNull(request);

        TelemetryTier tier = CoarsestMeeting(request.Resolution);
        string reason = request.Resolution is { } wanted
            ? $"coarsest tier at or below the requested {Describe(wanted)} resolution"
            : "raw samples requested";

        if (tier == TelemetryTier.Raw && !RawCovers(request, rawAvailableFromUtc))
        {
            tier = TelemetryTier.Second;
            reason = rawAvailableFromUtc is null
                ? "no raw samples are stored, so the finest rollup tier answered"
                : $"raw samples start at {rawAvailableFromUtc:u}, after the window began, so the finest rollup tier answered";
        }

        return Coarsen(tier, request, reason);
    }

    /// <summary>The coarsest tier whose bucket is no wider than the requested resolution.</summary>
    private static TelemetryTier CoarsestMeeting(TimeSpan? resolution)
    {
        if (resolution is not { } wanted || wanted <= TimeSpan.Zero) return TelemetryTier.Raw;

        TelemetryTier tier = TelemetryTier.Raw;
        foreach (TelemetryTier candidate in TelemetryTiers.Aggregated)
        {
            if (candidate.Resolution() <= wanted) tier = candidate;
        }

        return tier;
    }

    private static bool RawCovers(TieredQueryRequest request, DateTime? rawAvailableFromUtc) =>
        rawAvailableFromUtc is { } oldest
        && RollupIntervals.ToUtcTicks(oldest) <= RollupIntervals.ToUtcTicks(request.StartUtc);

    /// <summary>Steps coarser while the chosen tier would return more points than asked for.</summary>
    /// <remarks>
    /// Raw is left alone: how many samples a window holds depends on what the sensors did, and
    /// guessing would coarsen a sparse window that would have fitted comfortably. The store caps
    /// raw rows at read time instead and flags the answer as truncated.
    ///
    /// The incoming reason is kept and appended to rather than replaced. Both facts matter and they
    /// are not the same news: "this was coarsened so it would fit your chart" is a display decision
    /// the caller can change by asking for more points, while "your raw samples were pruned" is a
    /// permanent loss they cannot undo. Overwriting the second with the first told an operator
    /// their query had been tidied up, when what had actually happened is that the data was gone.
    /// </remarks>
    private static (TelemetryTier, string) Coarsen(
        TelemetryTier tier, TieredQueryRequest request, string reason)
    {
        long span = RollupIntervals.ToUtcTicks(request.EndUtc) - RollupIntervals.ToUtcTicks(request.StartUtc);

        while (tier != TelemetryTier.Raw && tier != TelemetryTier.Hour
               && span / tier.Resolution().Ticks > request.MaxPoints)
        {
            tier = tier == TelemetryTier.Second ? TelemetryTier.Minute : TelemetryTier.Hour;
            string coarsened = $"window holds more than {request.MaxPoints} points at a finer tier";
            reason = reason.Contains(coarsened, StringComparison.Ordinal) ? reason : $"{reason}; {coarsened}";
        }

        return (tier, reason);
    }

    private static string Describe(TimeSpan resolution) =>
        resolution.TotalSeconds < 1 ? $"{resolution.TotalMilliseconds:N0} ms" : $"{resolution.TotalSeconds:N0} s";
}
