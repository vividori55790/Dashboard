using System;
using System.Globalization;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// Writes a duration in the unit that reads without counting zeros.
/// </summary>
/// <remarks>
/// A power converter's interesting intervals span six orders of magnitude: a switching period is
/// microseconds, a dead time is nanoseconds, a soft-start is seconds. Printed in one fixed unit,
/// most of them come out as a leading run of zeros — and "0.0000123 s" is a number an operator has
/// to count digits to read, where reading it wrong by a factor of ten is the entire risk.
/// <para>
/// Kept in the portable half rather than beside the scope control that first needed it. It is
/// arithmetic and a unit table, with nothing in it about drawing, and the headless side has the
/// same intervals to report.
/// </para>
/// </remarks>
public static class IntervalFormat
{
    /// <summary>
    /// Seconds, milliseconds or microseconds, whichever keeps the mantissa readable.
    /// </summary>
    /// <remarks>
    /// Each boundary belongs to the larger unit, so nothing is ever written as "1000.00 µs". The
    /// sign is kept: on a scope it carries which cursor was placed first, which is how a lead is
    /// told from a lag.
    /// </remarks>
    public static string Seconds(double seconds)
    {
        if (!double.IsFinite(seconds)) return "—";

        double magnitude = Math.Abs(seconds);
        return magnitude switch
        {
            < 1e-3 => string.Create(CultureInfo.InvariantCulture, $"{seconds * 1e6:F2} µs"),
            < 1.0 => string.Create(CultureInfo.InvariantCulture, $"{seconds * 1e3:F3} ms"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{seconds:F4} s")
        };
    }
}
