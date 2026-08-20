using System;

namespace TelemetryDashboard.Core.Storage;

/// <summary>Where an answer came from: the samples themselves, or a summary of them.</summary>
/// <remarks>
/// Every query result carries one of these. A one-hour mean and a single 1 kHz sample are both
/// "a point on a chart" and are not remotely the same claim, so the tier travels with the data
/// instead of being inferred from how many points came back.
/// </remarks>
public enum TelemetryTier
{
    /// <summary>Individual samples, exactly as recorded.</summary>
    Raw = 0,

    /// <summary>One-second aggregates.</summary>
    Second = 1,

    /// <summary>One-minute aggregates.</summary>
    Minute = 2,

    /// <summary>One-hour aggregates.</summary>
    Hour = 3
}

/// <summary>Mapping between tiers and the rollup intervals that back them.</summary>
public static class TelemetryTiers
{
    /// <summary>Rollup tiers, finest first. <see cref="TelemetryTier.Raw"/> is excluded.</summary>
    public static readonly TelemetryTier[] Aggregated =
    {
        TelemetryTier.Second, TelemetryTier.Minute, TelemetryTier.Hour
    };

    /// <summary>The interval behind a tier, or null for <see cref="TelemetryTier.Raw"/>.</summary>
    public static RollupInterval? AsInterval(this TelemetryTier tier) => tier switch
    {
        TelemetryTier.Raw => null,
        TelemetryTier.Second => RollupInterval.Second,
        TelemetryTier.Minute => RollupInterval.Minute,
        TelemetryTier.Hour => RollupInterval.Hour,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown telemetry tier.")
    };

    /// <summary>The tier serving a rollup interval.</summary>
    public static TelemetryTier AsTier(this RollupInterval interval) => interval switch
    {
        RollupInterval.Second => TelemetryTier.Second,
        RollupInterval.Minute => TelemetryTier.Minute,
        RollupInterval.Hour => TelemetryTier.Hour,
        _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, "Unknown rollup interval.")
    };

    /// <summary>
    /// Width of one point at this tier. Zero for raw, whose spacing is whatever the sensor did.
    /// </summary>
    public static TimeSpan Resolution(this TelemetryTier tier) =>
        tier.AsInterval() is { } interval ? interval.Duration() : TimeSpan.Zero;
}
