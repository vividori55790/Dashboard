using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// What a channel is, how that was worked out, and how much of it is actually known.
/// </summary>
/// <remarks>
/// ARCHITECTURE's opening rule, applied to identity rather than to numbers: never assert what was
/// not measured. A kind is not a measurement, but it behaves like one downstream — it picks the
/// axis, the scale and the alarm band — so a confident wrong kind is the confident zero this
/// product exists to refuse, one level up. A channel named <c>t</c> whose values sit near 20 is not
/// a temperature; it is a channel nobody has identified, and that is a publishable answer.
/// <para>
/// So the three parts never travel apart. <see cref="Kind"/> without <see cref="Confidence"/> reads
/// as a fact; <see cref="Confidence"/> without <see cref="Why"/> cannot be argued with, and on a
/// plant floor the disputed value is the one that matters. <see cref="Evidence"/> is the
/// machine-readable half of <see cref="Why"/>, so a rule can check what prose cannot.
/// </para>
/// <para>
/// <b>The invariant.</b> <see cref="QuantityKind.Unclassified"/> and
/// <see cref="ClassificationConfidence.None"/> occur together and never apart. A kind at zero
/// confidence would render as a kind; a confidence above zero with no kind is a claim about
/// nothing. <see cref="Unknown"/> is the only way to spell that state, the same way
/// <c>ClockOffsetEstimate.Unmeasured</c> is the only way to spell a clock nobody compared.
/// </para>
/// </remarks>
/// <param name="Kind">The quantity, or <see cref="QuantityKind.Unclassified"/>.</param>
/// <param name="Unit">
/// The UCUM case-sensitive code for the declared unit, or null when none was declared or none was
/// recognised. UCUM is named rather than assumed, for the reason OPC-UA carries a
/// <c>namespaceUri</c> beside its unit id: a unit code without its system is not interpretable.
/// </param>
/// <param name="Subsystem">
/// The group the channel's name places it in, or null when the name places it in none. Null rather
/// than "default" or "": a rig where nothing declares a hierarchy has no subsystems, and inventing
/// one puts every channel in a group an operator never made.
/// </param>
/// <param name="Confidence">How well the kind is known. Only <c>High</c> is a fact.</param>
/// <param name="Evidence">What was consulted, and what disagreed.</param>
/// <param name="Why">A sentence an operator can read and dispute.</param>
public sealed record ChannelClassification(
    QuantityKind Kind,
    string? Unit,
    string? Subsystem,
    ClassificationConfidence Confidence,
    ClassificationEvidence Evidence,
    string Why)
{
    /// <summary>Nothing established a kind, and here is what would have.</summary>
    public static ChannelClassification Unknown(
        string why, string? subsystem = null,
        ClassificationEvidence evidence = ClassificationEvidence.None) =>
        new(QuantityKind.Unclassified, null, subsystem, ClassificationConfidence.None, evidence, why);

    /// <summary>
    /// Whether this must be presented as a proposal an operator accepts rather than as a fact.
    /// </summary>
    /// <remarks>
    /// True for everything below <see cref="ClassificationConfidence.High"/>, and deliberately true
    /// for <see cref="QuantityKind.Unclassified"/> as well — there is nothing to accept there, and a
    /// view that treats "not a proposal" as "settled" would render an unidentified channel as a
    /// confirmed one. The safe reading of this flag is the one that shows the operator more.
    /// </remarks>
    public bool IsProposal => Confidence != ClassificationConfidence.High;

    /// <summary>Whether two sources of evidence contradicted each other.</summary>
    /// <remarks>
    /// Surfaced separately from the confidence it lowers, because "weakly supported" and
    /// "actively disputed" are different things to put in front of an operator: the first wants
    /// more evidence and the second wants somebody to go and look at the device.
    /// </remarks>
    public bool HasConflict =>
        Evidence.HasFlag(ClassificationEvidence.NameDisagreesWithUnit)
        || Evidence.HasFlag(ClassificationEvidence.ValuesContradictKind);

    /// <summary>The kind as it goes on the wire, e.g. <c>electricPotential</c>.</summary>
    public string KindName => Camel(Kind.ToString());

    /// <summary>The confidence as it goes on the wire: <c>none</c>, <c>low</c>, <c>medium</c>, <c>high</c>.</summary>
    public string ConfidenceName => Camel(Confidence.ToString());

    /// <summary>
    /// The evidence flags as separate names, so a consumer reads a list rather than a bitfield.
    /// </summary>
    /// <remarks>
    /// Empty rather than <c>["none"]</c> when nothing was consulted. A list with an entry in it
    /// looks like evidence at a glance, which is the wrong impression to give for the case where
    /// there was none.
    /// </remarks>
    public IReadOnlyList<string> EvidenceNames =>
        Enum.GetValues<ClassificationEvidence>()
            .Where(flag => flag != ClassificationEvidence.None && Evidence.HasFlag(flag))
            .Select(flag => Camel(flag.ToString()))
            .ToArray();

    private static string Camel(string name) =>
        name.Length == 0
            ? name
            : char.ToLower(name[0], CultureInfo.InvariantCulture) + name[1..];
}
