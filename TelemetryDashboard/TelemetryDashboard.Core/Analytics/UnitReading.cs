namespace TelemetryDashboard.Core.Analytics;

/// <summary>What a declared unit says about the quantity, including that it says two things.</summary>
/// <remarks>
/// A declared unit is normally a derivation rather than an inference — a reading in volts
/// <em>is</em> an electric potential, and no amount of contrary naming changes that. The exceptions
/// are what this type exists for. A bare <c>g</c> is a gram or it is standard gravity, and a rig
/// reporting vibration in <c>g</c> and mass in <c>g</c> is not unusual; a bare <c>C</c> is coulomb
/// in UCUM and celsius in every control room. Collapsing either to one answer is the guess this
/// taxonomy refuses, so both candidates travel and something else has to resolve them.
/// </remarks>
/// <param name="Ucum">UCUM case-sensitive code for <paramref name="Kind"/>, or null if unrecognised.</param>
/// <param name="Kind">The first reading of the unit.</param>
/// <param name="AlternativeUcum">UCUM code for <paramref name="Alternative"/>, where one exists.</param>
/// <param name="Alternative">
/// A second reading, or <see cref="QuantityKind.Unclassified"/>. Unclassified with
/// <see cref="Ambiguous"/> set means the other reading is a quantity this vocabulary has no kind
/// for — a farad, a coulomb — which still disqualifies the first from deciding alone.
/// </param>
/// <param name="Ambiguous">Whether the unit names more than one quantity.</param>
/// <param name="Note">Why it is ambiguous, in words, for the operator-facing sentence.</param>
public readonly record struct UnitReading(
    string? Ucum,
    QuantityKind Kind,
    string? AlternativeUcum,
    QuantityKind Alternative,
    bool Ambiguous,
    string? Note)
{
    /// <summary>No unit was declared, or none this vocabulary knows.</summary>
    public static UnitReading Unrecognised { get; } =
        new(null, QuantityKind.Unclassified, null, QuantityKind.Unclassified, false, null);

    /// <summary>A unit that names exactly one quantity.</summary>
    public static UnitReading Of(string ucum, QuantityKind kind) =>
        new(ucum, kind, null, QuantityKind.Unclassified, false, null);

    /// <summary>A unit that names two, neither of which may be chosen without other evidence.</summary>
    public static UnitReading Either(
        string ucum, QuantityKind kind, string? alternativeUcum, QuantityKind alternative, string note) =>
        new(ucum, kind, alternativeUcum, alternative, true, note);

    /// <summary>Whether the unit was recognised at all.</summary>
    public bool Recognised => Kind != QuantityKind.Unclassified;

    /// <summary>Whether <paramref name="kind"/> is one of the readings this unit permits.</summary>
    public bool Permits(QuantityKind kind) => kind == Kind || kind == Alternative;

    /// <summary>The UCUM code to publish once <paramref name="kind"/> has been settled on.</summary>
    public string? UcumFor(QuantityKind kind) => kind == Alternative ? AlternativeUcum : Ucum;
}
