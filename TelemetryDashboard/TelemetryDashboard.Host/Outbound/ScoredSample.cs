using System;
using System.Collections.Generic;
using System.Linq;

namespace TelemetryDashboard.Host.Outbound;

/// <summary>
/// One published sample and the verdict reached about it, as seen by anything outside the host.
/// </summary>
/// <remarks>
/// <see cref="ZScore"/> and <see cref="IsAnomaly"/> are nullable, and that is the whole design.
/// During warm-up the analytics engine has no baseline and reports a z-score of 0 with
/// <c>IsAnomaly</c> false, which reads exactly like a calm channel. Carrying that as a number into
/// an alert relay or an MQTT topic would export a normality the host never established. Null means
/// "not judged", and every consumer has to decide what to do about it rather than being handed a
/// confident zero.
/// </remarks>
/// <summary>One limit this sample was evaluated against, and what it did to that rule.</summary>
/// <remarks>
/// The transition rather than a bare "is outside", because the three states are acted on
/// differently and merging them loses the distinction that matters. An interlock acts on the
/// crossing and then holds off — before that was true it wrote 91 identical commands in twenty
/// seconds on a live run, from a five-second cooldown the limit path had bypassed. An alert relay
/// wants the crossing <em>and</em> the recovery and nothing in between, because an operator paged
/// once when a converter left its band and once when it came back has been told the whole story,
/// while one paged per sample stops reading them.
/// </remarks>
public readonly record struct BreachedLimit(
    Core.Analytics.ChannelLimit Rule, Core.Analytics.LimitTransition Transition)
{
    /// <summary>Whether this sample is the moment the channel left the band.</summary>
    public bool JustEntered => Transition == Core.Analytics.LimitTransition.Entered;

    /// <summary>Whether the reading is outside the band, however long it has been.</summary>
    public bool IsOutside =>
        Transition is Core.Analytics.LimitTransition.Entered
                   or Core.Analytics.LimitTransition.Sustained;
}

public readonly record struct ScoredSample(
    string Channel,
    string NodeId,
    string Variable,
    double Value,
    string Unit,
    DateTime TimestampUtc,
    double? ZScore,
    bool? IsAnomaly,
    string? AnalyzerId,
    bool IsSimulated,

    /// <summary>Engineering limits this reading is outside, or empty.</summary>
    /// <remarks>
    /// Carried beside the verdict rather than folded into it, because they answer different
    /// questions and one of them is blind where the other is not: a z-score asks how unusual the
    /// reading is against the channel's own recent history, so a bus that settles above its
    /// ceiling stops being unusual within a minute. Anything downstream that acts on a machine
    /// needs the limit, not the score.
    /// </remarks>
    System.Collections.Generic.IReadOnlyList<BreachedLimit>? BreachedLimits = null)
{
    /// <summary>True when this reading is outside at least one declared limit.</summary>
    /// <remarks>
    /// Not simply "the list is non-empty": the list also carries the rules this sample brought
    /// back <em>inside</em>, and a recovery is the opposite of a breach.
    /// </remarks>
    public bool BreachesALimit => BreachedLimits is { Count: > 0 } && BreachedLimits.Any(l => l.IsOutside);

    /// <summary>Limits this sample crossed out of, and the ones it returned into.</summary>
    public IEnumerable<BreachedLimit> LimitTransitions =>
        BreachedLimits?.Where(l => l.Transition is Core.Analytics.LimitTransition.Entered
                                                or Core.Analytics.LimitTransition.Cleared)
        ?? Enumerable.Empty<BreachedLimit>();

    /// <summary>True when the host actually reached a judgement about this sample.</summary>
    public bool HasVerdict => ZScore is not null;

    /// <summary>One line describing the sample, used in alerts.</summary>
    /// <remarks>
    /// A limit breach is named first and by rule, because it is the actionable half: "outside
    /// 370..420 V" tells an operator what to do and "2.4 sigma" tells them the channel has been
    /// quiet lately.
    /// </remarks>
    public string Describe() => HasVerdict
        ? $"{Channel} = {Value:0.###}{UnitSuffix} ({ZScore:0.00} sigma, {AnalyzerId})"
        : $"{Channel} = {Value:0.###}{UnitSuffix} (no verdict: not enough history yet)";

    /// <summary>The limits this reading is outside, or empty when it is inside all of them.</summary>
    /// <remarks>
    /// Separate from <see cref="Describe"/> so a caller can place it. Appended inside the
    /// description it landed between the reading and the timestamp — "2.62 sigma) OUTSIDE LIMIT:
    /// grid.voltage[V] &lt; 300 at 2026-08-21" — which reads as though the limit had a time on it.
    /// </remarks>
    public string DescribeLimits() => BreachesALimit
        ? "Outside " + string.Join("; ", BreachedLimits!.Where(l => l.IsOutside).Select(l => l.Rule.Declaration))
        : string.Empty;

    private string UnitSuffix => string.IsNullOrEmpty(Unit) ? string.Empty : " " + Unit;
}
