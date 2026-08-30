using System;
using System.Globalization;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// Whether the values a channel has actually produced are impossible for a proposed kind.
/// </summary>
/// <remarks>
/// <b>Values may veto, never elect.</b> Nothing here can classify a channel; it can only take a
/// classification away. That asymmetry is the whole design, and it is the direct answer to the
/// failure ROADMAP W1 names: a channel whose values sit near 20 is not thereby a temperature, and
/// any table that lets a range propose a kind will eventually propose one for a pressure in bar, a
/// duty cycle in percent and a bus current, all of which also sit near 20.
/// <para>
/// <b>So only impossibilities are encoded, not plausibilities.</b> The list is deliberately, almost
/// disappointingly short, and every quantity absent from it is absent for a reason worth writing
/// down, because the reasons are exactly the ones a plausible-range table gets wrong:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Voltage, current and power are unbounded in sign.</b> The rig this product was written
/// against is a dual-active-bridge converter. Power flows both ways by design, and a negative bus
/// current is normal operation rather than a fault. A "voltages are positive" rule would fire on
/// the healthiest machine in the building.
/// </description></item>
/// <item><description>
/// <b>Pressure goes negative</b> whenever it is gauge pressure and something is under vacuum.
/// </description></item>
/// <item><description>
/// <b>Rotation goes negative</b> on any drive that reverses, and a length that is really a
/// displacement is signed about its datum. Mass reads negative on a load cell that has been tared.
/// In each case the quantity is fine and only a naive bound is wrong.
/// </description></item>
/// </list>
/// <para>
/// What is left is a floor nobody disputes — nothing is colder than absolute zero, nothing repeats
/// fewer than zero times a second, nothing occupies a negative number of bytes — plus one
/// convention check on ratios, which is marked as a convention rather than dressed up as physics.
/// </para>
/// </remarks>
public static class ValueRangeCheck
{
    /// <summary>
    /// The range outside which a reading contradicts the kind, or false when nothing bounds it.
    /// </summary>
    /// <remarks>
    /// Returning false is itself the reportable answer: it says the values were never checked,
    /// rather than checked and found consistent. A view that cannot tell those apart repeats this
    /// product's founding mistake at the smallest possible scale.
    /// </remarks>
    public static bool Bounds(QuantityKind kind, string? ucum, out double floor, out double ceiling)
    {
        floor = double.NegativeInfinity;
        ceiling = double.PositiveInfinity;

        switch (kind)
        {
            case QuantityKind.Temperature:
                // Per scale, because the floor is a property of the scale and not of temperature.
                floor = ucum switch
                {
                    "Cel" => -273.15,
                    "K" => 0.0,
                    "[degF]" => -459.67,
                    _ => double.NegativeInfinity
                };
                return double.IsFinite(floor);

            case QuantityKind.Frequency:
            case QuantityKind.DataSize:
                floor = 0.0;
                return true;

            case QuantityKind.Ratio when ucum is null or "1":
                // Not an impossibility and labelled as such below. Prometheus and OpenMetrics both
                // carry ratios as 0-1; a channel whose name says ratio and whose values run to 100
                // is on a percent scale, and picking either reading for it would be the guess.
                ceiling = 1.0;
                floor = 0.0;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// How the observed range is impossible for this kind, or null when it is not — including when
    /// nothing bounds the kind, and when there is nothing observed to check.
    /// </summary>
    public static string? Contradiction(QuantityKind kind, string? ucum, double? min, double? max)
    {
        if (min is not { } low || max is not { } high) return null;
        if (!double.IsFinite(low) || !double.IsFinite(high)) return null;
        if (!Bounds(kind, ucum, out double floor, out double ceiling)) return null;

        bool convention = kind == QuantityKind.Ratio;

        if (low < floor)
        {
            return convention
                ? $"values reach {Fixed(low)}, below the 0-1 scale a ratio is carried on"
                : $"values reach {Fixed(low)}, which is below the {Fixed(floor)} floor for this quantity";
        }

        if (high > ceiling)
        {
            return convention
                ? $"values reach {Fixed(high)}, above the 0-1 scale a ratio is carried on -- this "
                  + "looks like a percent, and which of the two it is cannot be read off the numbers"
                : $"values reach {Fixed(high)}, which is above the {Fixed(ceiling)} ceiling for this quantity";
        }

        return null;
    }

    private static string Fixed(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
