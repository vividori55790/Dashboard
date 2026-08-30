using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// The half of a classification an operator argues with.
/// </summary>
/// <remarks>
/// Split off because it is a different job from deciding, and because the register matters here in
/// a way it does not in the decision: these sentences are the only place a wrong classification can
/// be caught by a person. A verdict of "temperature, low confidence" invites a shrug; "no unit was
/// declared; the name word 'temp' proposes temperature, which a declared unit would confirm" tells
/// the operator both what happened and what to do about it.
/// <para>
/// Kept free of the machine-readable half deliberately. <see cref="ClassificationEvidence"/> exists
/// so a rule can check what prose cannot, and prose exists so a person can dispute what a flag
/// cannot express. Neither is a substitute for the other, and collapsing them would lose whichever
/// audience was not being thought about that day.
/// </para>
/// </remarks>
public static partial class ChannelClassifier
{
    private static string UnitBasis(
        UnitReading unit, string declared, IReadOnlyList<NameHint> hints, QuantityKind[] proposed)
    {
        // Phrased around UCUM's own "kind of quantity" column, which is where the right-hand half of
        // this sentence comes from.
        string basis = $"the declared unit '{declared}' is UCUM {unit.Ucum}, whose quantity is {Words(unit.Kind)}";

        if (proposed.Length == 0) return basis + ", and the name proposes nothing to check it against";

        if (proposed.Contains(unit.Kind))
        {
            return basis + $", and the name word '{Word(hints, unit.Kind)}' agrees";
        }

        return basis
            + $", but the name word '{hints[0].Word}' says {Words(hints[0].Kind)}. Both are written by "
            + "hand and this host cannot tell which is wrong, so the unit is carried as a proposal "
            + "rather than as a fact";
    }

    private static string AmbiguousBasis(UnitReading unit, string declared, QuantityKind? picked)
    {
        string basis = $"the declared unit '{declared}' names more than one quantity -- {unit.Note}";

        return picked is { } kind
            ? basis + $" -- and the name picks {Words(kind)}"
            : basis + ", and nothing in the name picks one";
    }

    private static string NameOnlyBasis(string declared, IReadOnlyList<NameHint> hints, QuantityKind kind) =>
        $"{Missing(declared)}; the name word '{Word(hints, kind)}' proposes {Words(kind)}, which a "
        + "declared unit would confirm";

    private static string SeveralBasis(string declared, QuantityKind[] proposed) =>
        $"{Missing(declared)}, and the name proposes "
        + string.Join(" and ", proposed.Select(Words))
        + " at once, so it proposes neither. Prometheus reads the last name component as the unit, "
        + "which would settle this; names reaching a hub were not necessarily written under that "
        + "convention, so it is not applied";

    private static string NothingBasis(string declared) =>
        $"{Missing(declared)}, and no word in the channel name is in the vocabulary. A routing rule "
        + "declaring a unit for this channel would classify it";

    /// <summary>
    /// Distinguishes a unit nobody sent from one that arrived and was not understood.
    /// </summary>
    /// <remarks>
    /// The two need different actions from an operator -- write a routing rule in the first case,
    /// work out why the rule already written declares something this vocabulary does not know in the
    /// second -- and collapsing them into "no unit" sends the second one looking in the wrong place.
    /// </remarks>
    private static string Missing(string declared) =>
        declared.Length == 0
            ? "no unit was declared"
            : $"the declared unit '{declared}' is not one this vocabulary recognises";

    private static string Word(IReadOnlyList<NameHint> hints, QuantityKind kind)
    {
        foreach (NameHint hint in hints)
        {
            if (hint.Kind == kind) return hint.Word;
        }

        return string.Empty;
    }

    /// <summary>A kind as an operator would say it: <c>electric potential</c>, not <c>ElectricPotential</c>.</summary>
    private static string Words(QuantityKind kind)
    {
        string name = kind.ToString();
        var text = new StringBuilder(name.Length + 4);

        foreach (char c in name)
        {
            if (char.IsUpper(c) && text.Length > 0) text.Append(' ');
            text.Append(char.ToLowerInvariant(c));
        }

        return text.ToString();
    }
}
