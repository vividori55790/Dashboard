using System;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>What one sample did to a rule that watches its channel.</summary>
public enum LimitTransition
{
    /// <summary>Inside the band, and was already.</summary>
    None,

    /// <summary>Outside the band, having been inside. The moment worth telling someone about.</summary>
    Entered,

    /// <summary>Still outside. Counted, not announced again.</summary>
    Sustained,

    /// <summary>Back inside, having been outside.</summary>
    Cleared,

    /// <summary>
    /// Not evaluated: the sample's unit disagrees with the one the limit was written in.
    /// </summary>
    /// <remarks>
    /// Its own outcome rather than folded into <see cref="None"/>, because the two are opposites. A
    /// rule that is quiet because everything is fine and a rule that is quiet because it can never
    /// fire look identical from outside, and only the second means the machine is unprotected.
    /// </remarks>
    UnitMismatch
}

/// <summary>
/// What a limit has seen, separated from the code that evaluates it.
/// </summary>
/// <remarks>
/// Every field here exists so a quiet rule can be told apart from a protected one: how many samples
/// it actually evaluated, whether it is disarmed by a unit it does not understand, when the current
/// excursion began, and which side was crossed.
/// </remarks>
public sealed partial class LimitMonitor
{
    /// <summary>One rule's standing with respect to one channel.</summary>
    public sealed record RuleState
    {
        public string Declaration { get; init; } = string.Empty;
        public string Channel { get; init; } = string.Empty;

        /// <summary>Whether the last evaluated sample was outside the band.</summary>
        public bool InBreach { get; init; }

        /// <summary>Samples evaluated against this rule on this channel.</summary>
        public long Evaluated { get; init; }

        /// <summary>Samples that were outside the band.</summary>
        public long Breaches { get; init; }

        /// <summary>Times the channel crossed from inside to outside.</summary>
        public long Entries { get; init; }

        /// <summary>The most recent reading evaluated, and when.</summary>
        public double? LastValue { get; init; }
        public DateTime? LastSeenUtc { get; init; }

        /// <summary>When the current breach began, or null when not in breach.</summary>
        public DateTime? BreachSinceUtc { get; init; }

        /// <summary>Which side was crossed, for the current or most recent breach.</summary>
        public string? Reason { get; init; }

        /// <summary>
        /// Set when this rule cannot fire because the channel reports a different unit.
        /// </summary>
        public string? UnitMismatch { get; init; }
    }

}
