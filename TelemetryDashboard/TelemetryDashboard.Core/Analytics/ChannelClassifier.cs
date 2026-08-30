using System.Collections.Generic;
using System.Linq;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// Works out what a channel is from its name, its declared unit and what it has produced.
/// </summary>
/// <remarks>
/// <b>The one invariant.</b> Only a declared unit that names exactly one quantity reaches
/// <see cref="ClassificationConfidence.High"/>. A name may propose, values may veto, and neither
/// can promote. That is not a tuning choice — it is the difference between a derivation and an
/// inference, and it is asserted by a rule rather than left to be respected: a reading in volts
/// <em>is</em> an electric potential, whereas a channel called <c>temp</c> is a channel somebody
/// abbreviated.
/// <para>
/// <b>Why a disagreement lowers rather than resolves.</b> A channel named <c>bus_voltage</c>
/// carrying a declared unit of <c>A</c> is a mislabel, and this host has no way to know which half
/// is the mistake — the rules file and the firmware are both hand-written by people. Picking the
/// unit and moving on would put a current axis on something an operator reads as a voltage, with
/// nothing on screen to argue with. So the unit's reading is kept, the confidence drops to a
/// proposal, and <see cref="ClassificationEvidence.NameDisagreesWithUnit"/> is set so a view can
/// show the row as disputed rather than merely weak.
/// </para>
/// <para>
/// <b>What an unclassified channel gets instead.</b> A sentence naming what would classify it. The
/// positional <c>field1</c> a rig produces before anybody writes a rules file is the case this is
/// for: there is no proposal to accept there and inventing one would be the defect, but "declare a
/// unit for it and it classifies" is a next step rather than a dead end.
/// </para>
/// </remarks>
public static partial class ChannelClassifier
{
    /// <summary>Classifies one channel, or says that nothing established what it is.</summary>
    /// <param name="channel">The routed channel name.</param>
    /// <param name="declaredUnit">The unit that arrived with it, if any.</param>
    /// <param name="observedMin">Lowest value seen, or null when nothing has been seen.</param>
    /// <param name="observedMax">Highest value seen, or null when nothing has been seen.</param>
    public static ChannelClassification Classify(
        string? channel, string? declaredUnit, double? observedMin = null, double? observedMax = null)
    {
        string? subsystem = SubsystemName.From(channel);
        UnitReading unit = UnitVocabulary.Read(declaredUnit);
        IReadOnlyList<NameHint> hints = ChannelNameHints.Read(channel);
        QuantityKind[] proposed = hints.Select(h => h.Kind).Distinct().ToArray();

        string declared = (declaredUnit ?? string.Empty).Trim();

        if (unit.Ambiguous)
        {
            return FromAmbiguousUnit(unit, declared, proposed, subsystem, observedMin, observedMax);
        }

        if (unit.Recognised)
        {
            return FromUnit(unit, declared, hints, proposed, subsystem, observedMin, observedMax);
        }

        return FromNameAlone(declared, hints, proposed, subsystem, observedMin, observedMax);
    }

    private static ChannelClassification FromUnit(
        UnitReading unit, string declared, IReadOnlyList<NameHint> hints, QuantityKind[] proposed,
        string? subsystem, double? min, double? max)
    {
        var evidence = ClassificationEvidence.DeclaredUnit;
        ClassificationConfidence confidence = ClassificationConfidence.High;

        if (proposed.Length > 0)
        {
            evidence |= ClassificationEvidence.ChannelName;
            if (!proposed.Contains(unit.Kind))
            {
                evidence |= ClassificationEvidence.NameDisagreesWithUnit;
                confidence = ClassificationConfidence.Low;
            }
        }

        return Settle(unit.Kind, unit.Ucum, subsystem, confidence, evidence, min, max,
            UnitBasis(unit, declared, hints, proposed));
    }

    private static ChannelClassification FromAmbiguousUnit(
        UnitReading unit, string declared, QuantityKind[] proposed,
        string? subsystem, double? min, double? max)
    {
        var evidence = ClassificationEvidence.DeclaredUnit | ClassificationEvidence.UnitIsAmbiguous;
        if (proposed.Length > 0) evidence |= ClassificationEvidence.ChannelName;

        QuantityKind[] picked = proposed.Where(unit.Permits).ToArray();

        // Exactly one, or the name has not resolved anything: two candidate readings and a name
        // pointing at both is the same amount of information as a name pointing at neither.
        if (picked.Length != 1)
        {
            return ChannelClassification.Unknown(
                AmbiguousBasis(unit, declared, null), subsystem, evidence);
        }

        QuantityKind kind = picked[0];
        return Settle(kind, unit.UcumFor(kind), subsystem, ClassificationConfidence.Medium, evidence,
            min, max, AmbiguousBasis(unit, declared, kind));
    }

    private static ChannelClassification FromNameAlone(
        string declared, IReadOnlyList<NameHint> hints, QuantityKind[] proposed,
        string? subsystem, double? min, double? max)
    {
        if (proposed.Length == 0) return ChannelClassification.Unknown(NothingBasis(declared), subsystem);

        if (proposed.Length > 1)
        {
            return ChannelClassification.Unknown(
                SeveralBasis(declared, proposed), subsystem,
                ClassificationEvidence.ChannelName | ClassificationEvidence.NameProposesSeveralKinds);
        }

        // No UCUM code: the kind is proposed and the unit is genuinely unknown, and filling one in
        // from the kind would put a unit on the wire that no device ever declared.
        return Settle(proposed[0], null, subsystem, ClassificationConfidence.Low,
            ClassificationEvidence.ChannelName, min, max, NameOnlyBasis(declared, hints, proposed[0]));
    }
}
