using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Services;

/// <summary>
/// The engineering unit a channel arrived with, said in Grafana's vocabulary.
/// </summary>
/// <remarks>
/// Grafana's unit identifiers are a closed vocabulary, not free text: an unrecognised id makes the
/// panel fall back to a bare number, so a plausible-looking guess such as <c>"volts"</c> or
/// <c>"degC"</c> silently loses the axis it was supposed to fix. Every id below was read out of
/// <c>packages/grafana-data/src/valueFormats/categories.ts</c> in grafana/grafana rather than
/// recalled, and the ones that look wrong — <c>amp</c> not <c>ampere</c>, <c>rotrpm</c> not
/// <c>rpm</c>, <c>s</c> not <c>sec</c>, <c>accG</c> with a capital G — are the reason why.
/// <para>
/// <b>Matched ordinally, and that is not fussiness.</b> <c>mV</c> is a millivolt and <c>MV</c> a
/// megavolt; a case-insensitive lookup differs by a factor of a billion. <see cref="Ingest.UnitScale"/>
/// already refuses to infer across that boundary for the same reason.
/// </para>
/// <para>
/// <b>An unrecognised unit becomes a number, never a guess.</b> This is W1's rule — answer
/// "unclassified" rather than pick a quantity kind from a name — applied at the one place where a
/// wrong answer becomes an axis label an operator will read as fact.
/// </para>
/// </remarks>
public static partial class GrafanaDashboardExport
{
    /// <summary>Grafana's identifier for "a number with no unit".</summary>
    public const string UnitlessNumber = "none";

    /// <summary>
    /// Wire unit to Grafana unit id, for the units this product's own paths actually produce.
    /// </summary>
    /// <remarks>
    /// Deliberately short. A table covering every unit in Grafana would be mostly entries nobody
    /// has ever sent, each one a chance to have mistyped an id that no test would ever reach.
    /// <para>
    /// <b>Deliberately absent: <c>g</c>.</b> Grafana has both <c>accG</c> (G unit, acceleration)
    /// and <c>massg</c> (gram), and this product uses <c>g</c> for both senses — the vibration
    /// channels mean acceleration, while <see cref="Ingest.UnitScale"/>'s base-unit allowlist has
    /// <c>g</c> among the masses and scales <c>mg</c> off it. Nothing on the wire distinguishes
    /// them, so a channel in <c>g</c> gets a plain number and keeps its raw unit in the panel
    /// title. Picking one would be the confident-classification failure W1 is written against, and
    /// which of the two it should be is a product decision rather than an engineering one.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> GrafanaUnits = new(StringComparer.Ordinal)
    {
        // Electrical. "F" is farad here because UnitScale's allowlist already says so for this
        // product; Fahrenheit arrives as "°F" and is listed separately below.
        ["V"] = "volt",
        ["mV"] = "mvolt",
        ["kV"] = "kvolt",
        ["A"] = "amp",
        ["mA"] = "mamp",
        ["kA"] = "kamp",
        ["W"] = "watt",
        ["mW"] = "mwatt",
        ["kW"] = "kwatt",
        ["VA"] = "voltamp",
        ["Wh"] = "watth",
        ["Ah"] = "amph",
        ["J"] = "joule",
        ["F"] = "farad",
        ["H"] = "henry",
        ["Ohm"] = "ohm",
        ["Ω"] = "ohm",
        ["mOhm"] = "mohm",
        ["kOhm"] = "kohm",

        // Temperature. Bare "C" is not here: it is coulomb as often as celsius, and this is
        // exactly the ambiguity the unit column exists to remove rather than to re-introduce.
        ["°C"] = "celsius",
        ["°F"] = "fahrenheit",
        ["K"] = "kelvin",

        // Rate and rotation.
        ["Hz"] = "hertz",
        ["rpm"] = "rotrpm",
        ["RPM"] = "rotrpm",

        // Time.
        ["ns"] = "ns",
        ["µs"] = "µs",
        ["ms"] = "ms",
        ["s"] = "s",

        // Ratio. Not rescaled to Prometheus's preferred 0-1 ratio: this hub relays what a device
        // reported and has no way to know whether a "%" channel is 0-100 or 0-1, so dividing by a
        // hundred here would be asserting a scale nobody measured. Grafana's "percent" is the
        // 0-100 form, which is what firmware overwhelmingly sends.
        ["%"] = "percent",

        // Pressure, length, velocity, mass, flow.
        ["Pa"] = "pressurepa",
        ["bar"] = "pressurebar",
        ["mm"] = "lengthmm",
        ["m"] = "lengthm",
        ["km"] = "lengthkm",
        ["m/s"] = "velocityms",
        ["mg"] = "massmg",
        ["kg"] = "masskg",
        ["L/min"] = "flowlpm",
        ["L/h"] = "litreh"
    };

    /// <summary>
    /// Grafana's id for <paramref name="wireUnit"/>, or <see cref="UnitlessNumber"/>.
    /// </summary>
    /// <remarks>
    /// A channel that arrived without a unit and one whose unit this table does not know come back
    /// the same, and that is correct: neither establishes what quantity is being plotted, and the
    /// panel title carries the raw string in both cases so an operator can see what was not
    /// understood rather than being told there was nothing to understand.
    /// </remarks>
    public static string UnitId(string? wireUnit)
    {
        string trimmed = (wireUnit ?? string.Empty).Trim();
        if (trimmed.Length == 0) return UnitlessNumber;

        return GrafanaUnits.TryGetValue(trimmed, out string? id) ? id : UnitlessNumber;
    }

    /// <summary>Whether this export understood the unit, as opposed to falling back.</summary>
    /// <remarks>
    /// Separate from <see cref="UnitId"/> because a channel legitimately reporting a dimensionless
    /// ratio and a channel whose unit nobody recognised both plot as plain numbers, and only the
    /// second is worth an operator's attention.
    /// </remarks>
    public static bool UnitRecognised(string? wireUnit) =>
        GrafanaUnits.ContainsKey((wireUnit ?? string.Empty).Trim());
}
