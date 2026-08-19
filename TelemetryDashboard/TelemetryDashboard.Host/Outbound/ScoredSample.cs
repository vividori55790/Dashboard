using System;

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
    bool IsSimulated)
{
    /// <summary>True when the host actually reached a judgement about this sample.</summary>
    public bool HasVerdict => ZScore is not null;

    /// <summary>One line describing the sample, used in alerts.</summary>
    public string Describe() => HasVerdict
        ? $"{Channel} = {Value:0.###}{UnitSuffix} ({ZScore:0.00} sigma, {AnalyzerId})"
        : $"{Channel} = {Value:0.###}{UnitSuffix} (no verdict: not enough history yet)";

    private string UnitSuffix => string.IsNullOrEmpty(Unit) ? string.Empty : " " + Unit;
}
