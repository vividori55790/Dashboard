using System;
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
/// <summary>One limit a reading is outside, and whether this sample is the crossing.</summary>
/// <remarks>
/// The distinction is what lets an interlock act once on a crossing and then hold off, instead of
/// writing a command per sample for as long as the condition lasts. Measured on a live loopback
/// run before this existed: 91 identical commands in twenty seconds, from a five-second cooldown
/// that the limit path had bypassed entirely.
/// </remarks>
public readonly record struct BreachedLimit(Core.Analytics.ChannelLimit Rule, bool JustEntered);

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
    public bool BreachesALimit => BreachedLimits is { Count: > 0 };

    /// <summary>True when the host actually reached a judgement about this sample.</summary>
    public bool HasVerdict => ZScore is not null;

    /// <summary>One line describing the sample, used in alerts.</summary>
    /// <remarks>
    /// A limit breach is named first and by rule, because it is the actionable half: "outside
    /// 370..420 V" tells an operator what to do and "2.4 sigma" tells them the channel has been
    /// quiet lately.
    /// </remarks>
    public string Describe()
    {
        string limits = BreachesALimit
            ? " OUTSIDE LIMIT: " + string.Join("; ", BreachedLimits!.Select(l => l.Rule.Declaration))
            : string.Empty;

        return (HasVerdict
            ? $"{Channel} = {Value:0.###}{UnitSuffix} ({ZScore:0.00} sigma, {AnalyzerId})"
            : $"{Channel} = {Value:0.###}{UnitSuffix} (no verdict: not enough history yet)") + limits;
    }

    private string UnitSuffix => string.IsNullOrEmpty(Unit) ? string.Empty : " " + Unit;
}
