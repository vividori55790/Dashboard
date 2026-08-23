using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Ingest;

/// <summary>
/// Whether two engineering units are the same quantity at a different scale.
/// </summary>
/// <remarks>
/// The single most likely thing to be wrong when a real MCU meets a profile written for the rig
/// rather than for the firmware: the device reports 48259.9 mV and the band is stated in volts. It
/// is not a rounding problem — the reading is off by a factor of a thousand, the band cannot fire,
/// and a band that cannot fire looks exactly like a machine that is behaving.
/// <para>
/// This is a derivation and not a guess, which is why the drafted rules may fill in the gain it
/// returns. mV to V is 0.001 by definition. Nothing else here infers anything: two units that are
/// not the same base at a different prefix are simply unrelated, and the operator is asked.
/// </para>
/// <para>
/// The base units are an allowlist rather than "whatever is left after removing a prefix". Without
/// one, <c>min</c> reads as milli-inches and <c>mol</c> as milli-litres-of-something — a prefix
/// parser applied to arbitrary text invents relationships, and inventing one here would silently
/// scale a reading by a thousand.
/// </para>
/// </remarks>
public static class UnitScale
{
    private static readonly Dictionary<string, double> Prefixes = new(StringComparer.Ordinal)
    {
        ["p"] = 1e-12,
        ["n"] = 1e-9,
        ["u"] = 1e-6,
        ["µ"] = 1e-6,
        ["m"] = 1e-3,
        ["c"] = 1e-2,
        ["k"] = 1e3,
        ["M"] = 1e6,
        ["G"] = 1e9
    };

    private static readonly HashSet<string> BaseUnits = new(StringComparer.Ordinal)
    {
        "V", "A", "W", "Wh", "Ah", "VA", "var",
        "Hz", "s", "F", "H", "J", "C", "N", "T",
        "Ohm", "Pa", "bar", "g", "m", "L"
    };

    /// <summary>
    /// The factor a reading in <paramref name="from"/> must be multiplied by to be in
    /// <paramref name="to"/>, or null when the two are not the same quantity.
    /// </summary>
    public static double? Between(string? from, string? to)
    {
        string source = (from ?? string.Empty).Trim();
        string target = (to ?? string.Empty).Trim();

        if (source.Length == 0 || target.Length == 0) return null;
        if (string.Equals(source, target, StringComparison.Ordinal)) return 1.0;

        if (!TryScale(source, out double sourceScale, out string sourceBase)) return null;
        if (!TryScale(target, out double targetScale, out string targetBase)) return null;

        return string.Equals(sourceBase, targetBase, StringComparison.Ordinal)
            ? sourceScale / targetScale
            : null;
    }

    /// <summary>Splits a unit into its scale and its base, e.g. mV into 0.001 and V.</summary>
    private static bool TryScale(string unit, out double scale, out string baseUnit)
    {
        scale = 1.0;
        baseUnit = unit;

        if (BaseUnits.Contains(unit)) return true;

        // One character of prefix. There is no two-character SI prefix in the range that matters
        // here, and accepting "da" would make "dagger" a unit.
        string head = unit[..1];
        string tail = unit[1..];

        if (!Prefixes.TryGetValue(head, out double prefix) || !BaseUnits.Contains(tail)) return false;

        scale = prefix;
        baseUnit = tail;
        return true;
    }

    /// <summary>How a gain reads in a rules file: 0.001 rather than 1E-03.</summary>
    public static string Format(double gain) =>
        gain.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture);
}
