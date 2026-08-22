using System;
using System.Globalization;
using TelemetryDashboard.Host.Configuration;

namespace TelemetryDashboard.Host.Backtest;

/// <summary>
/// Reading typed values off the backtest command line.
/// </summary>
/// <remarks>
/// Built on <see cref="ArgumentCursor"/>, which the server parser already uses, so both halves of
/// this executable reject a missing value the same way. The typed readers are here rather than
/// there because only this subcommand takes dates and rates.
/// <para>
/// Every conversion is invariant. The host sets <c>InvariantGlobalization</c>, but that is a reason
/// to be explicit rather than a substitute for it: a rate typed as 2.5 must not become 25 because
/// the machine's locale writes decimals with a comma, and a silent success is how it would.
/// </para>
/// </remarks>
internal static class BacktestValues
{
    /// <summary>Reads the following argument as a decimal number.</summary>
    public static bool Number(string[] args, ref int index, out double value)
    {
        value = 0;
        return ArgumentCursor.TryValue(args, ref index, out string text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }

    /// <summary>Reads the following argument as a whole number of bars.</summary>
    public static bool Count(string[] args, ref int index, out int value)
    {
        value = 0;
        return ArgumentCursor.TryValue(args, ref index, out string text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Reads the following argument as a calendar date.</summary>
    /// <remarks>
    /// Exact format first. A lenient parse of "03-04-2020" is a different day depending on the
    /// machine, and a window shifted by three months is not an error anyone notices in a result.
    /// </remarks>
    public static bool Date(string[] args, ref int index, out DateOnly value)
    {
        value = default;
        if (!ArgumentCursor.TryValue(args, ref index, out string text)) return false;

        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out value)
            || DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }
}
