using System;

namespace TelemetryDashboard.Core.Storage;

/// <summary>Aggregation window widths kept alongside the raw samples.</summary>
/// <remarks>
/// The numeric values are persisted in the <c>interval_code</c> column, so they are fixed: renaming
/// a member is free, renumbering one silently re-labels every stored window.
/// </remarks>
public enum RollupInterval
{
    /// <summary>One second.</summary>
    Second = 1,

    /// <summary>One minute.</summary>
    Minute = 2,

    /// <summary>One hour.</summary>
    Hour = 3
}

/// <summary>Bucket arithmetic for <see cref="RollupInterval"/>.</summary>
/// <remarks>
/// Every tier divides evenly into a day and <see cref="DateTime.Ticks"/> counts from midnight on
/// 0001-01-01, so aligning by remainder lands exactly on a UTC second, minute or hour boundary —
/// no calendar arithmetic, and no drift as the buckets march forward.
/// </remarks>
public static class RollupIntervals
{
    /// <summary>All tiers, finest first.</summary>
    public static readonly RollupInterval[] All =
    {
        RollupInterval.Second, RollupInterval.Minute, RollupInterval.Hour
    };

    /// <summary>Width of one bucket, in ticks.</summary>
    public static long TicksPer(this RollupInterval interval) => interval switch
    {
        RollupInterval.Second => TimeSpan.TicksPerSecond,
        RollupInterval.Minute => TimeSpan.TicksPerMinute,
        RollupInterval.Hour => TimeSpan.TicksPerHour,
        _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, "Unknown rollup interval.")
    };

    /// <summary>Width of one bucket.</summary>
    public static TimeSpan Duration(this RollupInterval interval) => new(interval.TicksPer());

    /// <summary>Start of the bucket containing <paramref name="utcTicks"/>.</summary>
    public static long BucketStartTicks(this RollupInterval interval, long utcTicks)
    {
        long width = interval.TicksPer();
        long remainder = utcTicks % width;

        // DateTime ticks are never negative, but a caller can hand this method a raw offset. Floor
        // rather than truncate, so a pre-epoch instant lands in the bucket that contains it instead
        // of the one after it.
        return remainder >= 0 ? utcTicks - remainder : utcTicks - remainder - width;
    }

    /// <summary>
    /// Converts a timestamp to the UTC tick count buckets are aligned on.
    /// </summary>
    /// <remarks>
    /// <see cref="DateTimeKind.Unspecified"/> is taken as already-UTC, exactly as
    /// <c>SqliteTelemetrySchema.ToUtcTicks</c> does for the raw store. Calling
    /// <see cref="DateTime.ToUniversalTime"/> on it would shift the sample by whatever offset the
    /// machine happens to be in, so the same recording would fall into different buckets on two
    /// machines — and the rollups would disagree with the raw rows they were computed from.
    /// </remarks>
    public static long ToUtcTicks(DateTime timestamp) => timestamp.Kind switch
    {
        DateTimeKind.Local => timestamp.ToUniversalTime().Ticks,
        _ => timestamp.Ticks
    };
}
