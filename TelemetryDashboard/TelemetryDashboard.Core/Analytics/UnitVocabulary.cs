using TelemetryDashboard.Core.Ingest;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// Reads a declared engineering unit as a quantity, in UCUM's spelling.
/// </summary>
/// <remarks>
/// The only route to <see cref="ClassificationConfidence.High"/>, because it is the only input that
/// is a derivation rather than an inference: a device that says volts has said electric potential.
/// Everything else here — the channel name, the observed values — is circumstantial.
/// <para>
/// <b>The prefix walk is borrowed rather than rewritten.</b> <see cref="UnitScale"/> already knows
/// that <c>mV</c> is a thousandth of a <c>V</c> and that <c>min</c> is not milli-inches, and it
/// knows it against an allowlist of base units precisely so a prefix parser cannot invent
/// relationships. Asking it whether a declared unit converts to each known base is how the base is
/// found here; a second prefix table would drift from that one and the drift would be silent.
/// </para>
/// <para>
/// <b>Spellings a device actually emits, not the ones UCUM prescribes.</b> UCUM says <c>Cel</c>;
/// firmware says <c>°C</c>, <c>degC</c> and <c>C</c>. The table maps what arrives onto what UCUM
/// calls it, which is the whole point of publishing a canonical code beside the declared text —
/// the declared text stays visible on <c>/api/inputs</c> so nobody has to trust the mapping blind.
/// </para>
/// <para>
/// <b>What is deliberately not here.</b> <c>deg</c> is not mapped: it is degrees of angle as often
/// as degrees of temperature, and there is no way to tell from the string. <c>B</c> is not mapped:
/// UCUM spells byte <c>By</c> and reserves <c>B</c> for the bel. Neither is worth a guess, and both
/// are the kind of thing that would be wrong on exactly one rig and wrong expensively.
/// </para>
/// </remarks>
public static partial class UnitVocabulary
{
    /// <summary>What the declared unit says, or <see cref="UnitReading.Unrecognised"/>.</summary>
    public static UnitReading Read(string? declared)
    {
        string unit = (declared ?? string.Empty).Trim();
        if (unit.Length == 0) return UnitReading.Unrecognised;

        if (Exact.TryGetValue(unit, out UnitReading exact)) return exact;
        if (Loose.TryGetValue(unit, out UnitReading loose)) return loose;

        foreach ((string ucum, QuantityKind kind) in Bases)
        {
            // A non-null factor is UnitScale saying these are the same quantity at a different
            // prefix, which is the only evidence needed: mV is a V and so it is a potential.
            if (UnitScale.Between(unit, ucum) is not null) return UnitReading.Of(unit, kind);
        }

        return UnitReading.Unrecognised;
    }
}

