namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// The stage where what a channel has actually produced gets a say — and only a negative one.
/// </summary>
/// <remarks>
/// Separated from the decision ladder because it is the asymmetry the taxonomy rests on and it is
/// worth being able to point at. Everything in the other file weighs a name against a unit; this
/// runs afterwards, on whatever they concluded, and can only ever take confidence away.
/// <para>
/// The tempting inversion is to let a range that fits a kind raise the confidence of a proposal.
/// It is refused for the reason ROADMAP W1 gives: values near 20 fit a temperature, a pressure in
/// bar, a duty cycle in percent and a bus current equally well, so "the range is consistent" is not
/// evidence for any one of them. Consistency is the absence of a contradiction, and this reports it
/// as exactly that — <see cref="ClassificationEvidence.ObservedValues"/> says the check ran, and
/// nothing says it passed with distinction.
/// </para>
/// </remarks>
public static partial class ChannelClassifier
{
    /// <summary>Applies the observed values, which may lower a verdict and may never raise one.</summary>
    private static ChannelClassification Settle(
        QuantityKind kind, string? ucum, string? subsystem, ClassificationConfidence confidence,
        ClassificationEvidence evidence, double? min, double? max, string basis)
    {
        // Nothing observed, or nothing about this kind that a range could contradict. The evidence
        // flag stays clear either way, so "never checked" cannot be read as "checked and fine".
        bool checkable = min is not null && max is not null
            && ValueRangeCheck.Bounds(kind, ucum, out _, out _);

        if (!checkable)
        {
            return new ChannelClassification(kind, ucum, subsystem, confidence, evidence, basis);
        }

        evidence |= ClassificationEvidence.ObservedValues;
        if (ValueRangeCheck.Contradiction(kind, ucum, min, max) is not { } contradiction)
        {
            return new ChannelClassification(kind, ucum, subsystem, confidence, evidence, basis);
        }

        evidence |= ClassificationEvidence.ValuesContradictKind;
        string why = basis + " -- but " + contradiction;

        // A name was the only thing holding a name-only verdict up, and the values have just taken
        // it away. A declared unit survives the contradiction as a disputed proposal, because the
        // device did say what it said, and discarding that would leave an operator with less to go
        // on than the device gave them.
        return evidence.HasFlag(ClassificationEvidence.DeclaredUnit)
            ? new ChannelClassification(
                kind, ucum, subsystem, ClassificationConfidence.Low, evidence, why)
            : ChannelClassification.Unknown(why, subsystem, evidence);
    }
}
