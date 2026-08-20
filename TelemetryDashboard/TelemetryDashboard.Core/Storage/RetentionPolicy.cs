using System;

namespace TelemetryDashboard.Core.Storage;

/// <summary>
/// How long each tier is kept. Deleting raw samples is irreversible, so this is opt-in.
/// </summary>
/// <remarks>
/// <see cref="Enabled"/> defaults to false and <see cref="Disabled"/> is what a store is
/// constructed with when the caller says nothing. A first run therefore deletes nothing, ever:
/// the failure mode of keeping too much is a large file, and the failure mode of the other default
/// is a recording that quietly destroyed the hours somebody needed. A prune asked for under a
/// disabled policy still reports what it <em>would</em> remove, which is how an operator sizes the
/// window before arming it.
/// </remarks>
public sealed record RetentionPolicy
{
    /// <summary>Whether a prune may actually delete. False means every prune is a dry run.</summary>
    public bool Enabled { get; init; }

    /// <summary>How much raw sample history to keep once <see cref="Enabled"/> is set.</summary>
    /// <remarks>
    /// Seven days is the value a caller gets if it enables pruning without stating a window. It is
    /// a starting point, not a recommendation — the right window is a function of disk size and of
    /// how far back anyone actually zooms.
    /// </remarks>
    public TimeSpan RawRetention { get; init; } = TimeSpan.FromDays(7);

    /// <summary>How long one-second windows are kept. Null keeps them indefinitely.</summary>
    public TimeSpan? SecondRetention { get; init; }

    /// <summary>How long one-minute windows are kept. Null keeps them indefinitely.</summary>
    public TimeSpan? MinuteRetention { get; init; }

    /// <summary>How long one-hour windows are kept. Null keeps them indefinitely.</summary>
    public TimeSpan? HourRetention { get; init; }

    /// <summary>The policy a store uses when none is supplied: nothing is ever deleted.</summary>
    public static RetentionPolicy Disabled { get; } = new();

    /// <summary>Retention for one rollup tier, or null when that tier is kept indefinitely.</summary>
    public TimeSpan? RetentionFor(RollupInterval interval) => interval switch
    {
        RollupInterval.Second => SecondRetention,
        RollupInterval.Minute => MinuteRetention,
        RollupInterval.Hour => HourRetention,
        _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, "Unknown rollup interval.")
    };

    /// <summary>
    /// Returns this policy, or throws when a stated window is not usable.
    /// </summary>
    /// <remarks>
    /// A negative or zero window would put the cutoff at or after "now" and delete everything
    /// including the sample that arrived a moment ago. A policy that means "keep nothing" has to be
    /// said out loud, not arrived at by a sign error, so it is rejected here.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">A retention window is not positive.</exception>
    public RetentionPolicy Validated()
    {
        Require(RawRetention, nameof(RawRetention));
        foreach (RollupInterval interval in RollupIntervals.All)
        {
            TimeSpan? window = RetentionFor(interval);
            if (window.HasValue) Require(window.Value, interval + "Retention");
        }

        return this;
    }

    private static void Require(TimeSpan window, string name)
    {
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                name, window, "A retention window must be positive; use a disabled policy to keep everything.");
        }
    }
}
