using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// The unit spellings this hub recognises, and what UCUM calls each of them.
/// </summary>
/// <remarks>
/// Split from the lookup because it is a list rather than a decision, and because it is the part
/// that grows: every rig that arrives with a spelling nobody anticipated adds a line here and
/// changes nothing else. Keeping it beside <see cref="UnitVocabulary.Read"/> would make the
/// interesting fifteen lines of that file scroll off the bottom of a screen full of table.
/// </remarks>
public static partial class UnitVocabulary
{
    /// <summary>
    /// Spellings whose case carries the meaning, so they cannot go in the tolerant table.
    /// </summary>
    /// <remarks>
    /// <c>C</c>, <c>F</c> and <c>g</c> are the three that bite. UCUM disambiguates gram from
    /// standard gravity by bracketing the customary unit — <c>g</c> against <c>[g]</c> — and a
    /// device emitting a bare <c>g</c> for vibration has discarded that distinction before this
    /// code ever sees it. <c>mg</c> is here for the same reason: milli-g is a normal way to report
    /// vibration, and reading it confidently as milligrams would put a mass axis on an
    /// accelerometer.
    /// </remarks>
    private static readonly Dictionary<string, UnitReading> Exact = new(StringComparer.Ordinal)
    {
        ["K"] = UnitReading.Of("K", QuantityKind.Temperature),
        ["Cel"] = UnitReading.Of("Cel", QuantityKind.Temperature),
        ["C"] = UnitReading.Either("Cel", QuantityKind.Temperature, null, QuantityKind.Unclassified,
            "'C' is UCUM's code for the coulomb; a device meaning celsius should declare 'Cel'"),
        ["F"] = UnitReading.Either("[degF]", QuantityKind.Temperature, null, QuantityKind.Unclassified,
            "'F' is UCUM's code for the farad; a device meaning fahrenheit should declare '[degF]'"),
        ["g"] = UnitReading.Either("g", QuantityKind.Mass, "[g]", QuantityKind.Acceleration,
            "'g' is the gram in UCUM and standard gravity when it is vibration"),
        ["mg"] = UnitReading.Either("mg", QuantityKind.Mass, null, QuantityKind.Acceleration,
            "'mg' is the milligram, and milli-g is a normal way to report vibration")
    };

    /// <summary>Spellings where case is noise: <c>RPM</c>, <c>rpm</c> and <c>Rpm</c> are one unit.</summary>
    private static readonly Dictionary<string, UnitReading> Loose = new(StringComparer.OrdinalIgnoreCase)
    {
        ["°C"] = UnitReading.Of("Cel", QuantityKind.Temperature),
        ["degC"] = UnitReading.Of("Cel", QuantityKind.Temperature),
        ["celsius"] = UnitReading.Of("Cel", QuantityKind.Temperature),
        ["°F"] = UnitReading.Of("[degF]", QuantityKind.Temperature),
        ["degF"] = UnitReading.Of("[degF]", QuantityKind.Temperature),
        ["fahrenheit"] = UnitReading.Of("[degF]", QuantityKind.Temperature),
        ["kelvin"] = UnitReading.Of("K", QuantityKind.Temperature),

        ["%"] = UnitReading.Of("%", QuantityKind.Ratio),
        ["pct"] = UnitReading.Of("%", QuantityKind.Ratio),
        ["percent"] = UnitReading.Of("%", QuantityKind.Ratio),

        ["rpm"] = UnitReading.Of("/min", QuantityKind.RotationalFrequency),
        ["r/min"] = UnitReading.Of("/min", QuantityKind.RotationalFrequency),
        ["rev/min"] = UnitReading.Of("/min", QuantityKind.RotationalFrequency),

        ["ohm"] = UnitReading.Of("Ohm", QuantityKind.ElectricResistance),
        ["ohms"] = UnitReading.Of("Ohm", QuantityKind.ElectricResistance),
        ["Ω"] = UnitReading.Of("Ohm", QuantityKind.ElectricResistance),

        ["m/s2"] = UnitReading.Of("m/s2", QuantityKind.Acceleration),
        ["m/s^2"] = UnitReading.Of("m/s2", QuantityKind.Acceleration),
        ["m/s²"] = UnitReading.Of("m/s2", QuantityKind.Acceleration),

        ["byte"] = UnitReading.Of("By", QuantityKind.DataSize),
        ["bytes"] = UnitReading.Of("By", QuantityKind.DataSize),

        ["min"] = UnitReading.Of("min", QuantityKind.Time),
        ["minute"] = UnitReading.Of("min", QuantityKind.Time),
        ["hr"] = UnitReading.Of("h", QuantityKind.Time),
        ["hour"] = UnitReading.Of("h", QuantityKind.Time),

        ["volt"] = UnitReading.Of("V", QuantityKind.ElectricPotential),
        ["volts"] = UnitReading.Of("V", QuantityKind.ElectricPotential),
        ["amp"] = UnitReading.Of("A", QuantityKind.ElectricCurrent),
        ["amps"] = UnitReading.Of("A", QuantityKind.ElectricCurrent),
        ["ampere"] = UnitReading.Of("A", QuantityKind.ElectricCurrent),
        ["watt"] = UnitReading.Of("W", QuantityKind.Power),
        ["watts"] = UnitReading.Of("W", QuantityKind.Power),
        ["hertz"] = UnitReading.Of("Hz", QuantityKind.Frequency),
        ["psi"] = UnitReading.Of("[psi]", QuantityKind.Pressure),

        ["1"] = UnitReading.Of("1", QuantityKind.Dimensionless),
        ["unitless"] = UnitReading.Of("1", QuantityKind.Dimensionless),
        ["dimensionless"] = UnitReading.Of("1", QuantityKind.Dimensionless)
    };

    /// <summary>
    /// SI bases <see cref="UnitScale"/> already recognises, paired with the quantity UCUM's
    /// kind-of-quantity column gives them. Prefixed spellings reach these through the walk.
    /// </summary>
    /// <remarks>
    /// Several units <see cref="UnitScale"/> knows are absent — <c>F</c>, <c>H</c>, <c>N</c>,
    /// <c>T</c>, <c>C</c>, <c>VA</c>, <c>var</c>, <c>L</c>, <c>Ah</c>. That is not an oversight:
    /// this enum has no kind for capacitance, inductance, force, magnetic flux density, charge,
    /// apparent power, reactive power, volume or electric charge, and mapping them onto the nearest
    /// available kind would publish a wrong one. They come back unrecognised, which is true.
    /// </remarks>
    private static readonly (string Ucum, QuantityKind Kind)[] Bases =
    [
        ("V", QuantityKind.ElectricPotential),
        ("A", QuantityKind.ElectricCurrent),
        ("Ohm", QuantityKind.ElectricResistance),
        ("W", QuantityKind.Power),
        ("J", QuantityKind.Energy),
        ("Wh", QuantityKind.Energy),
        ("Hz", QuantityKind.Frequency),
        ("Pa", QuantityKind.Pressure),
        ("bar", QuantityKind.Pressure),
        ("s", QuantityKind.Time),
        ("m", QuantityKind.Length),
        ("g", QuantityKind.Mass)
    ];
}
