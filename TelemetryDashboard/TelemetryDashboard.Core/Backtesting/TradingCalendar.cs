using System;

namespace TelemetryDashboard.Core.Backtesting;

/// <summary>
/// How many bars a year of this series contains, measured from the series rather than assumed.
/// </summary>
/// <remarks>
/// Every annualised figure — volatility, Sharpe — is the per-bar figure multiplied by the square
/// root of this number, so getting it wrong scales the headline ratio by a constant and nothing in
/// the output looks unusual. The textbook constant is 252, which is right for US daily equity bars
/// and wrong for every other case a person will actually load: weekly bars, an exchange with
/// different holidays, a file with gaps, an hourly series, a crypto pair that trades on Sundays.
/// Assuming 252 for weekly data overstates the annualised volatility by more than a factor of two.
/// <para>
/// Counting the bars and dividing by the calendar time they span gets all of those right without
/// being told which one it was handed, and it is exactly as correct as the constant in the case the
/// constant was written for.
/// </para>
/// </remarks>
public static class TradingCalendar
{
    /// <summary>Days in a mean Gregorian year, leap years included.</summary>
    public const double DaysPerYear = 365.25;

    /// <summary>Calendar years between the two dates.</summary>
    public static double YearsBetween(DateOnly first, DateOnly last) =>
        (last.DayNumber - first.DayNumber) / DaysPerYear;

    /// <summary>
    /// Bars per year implied by <paramref name="barCount"/> bars spanning <paramref name="first"/>
    /// to <paramref name="last"/>.
    /// </summary>
    /// <remarks>
    /// Falls back to 252 only when the span is too short to measure — a handful of bars inside one
    /// week — where any answer is a guess and the conventional guess is at least the one a reader
    /// will recognise. The report says when this happened rather than leaving it implied.
    /// </remarks>
    public static double BarsPerYear(int barCount, DateOnly first, DateOnly last)
    {
        double years = YearsBetween(first, last);
        if (barCount < 2 || years <= 0) return 252.0;

        double perYear = (barCount - 1) / years;
        return double.IsFinite(perYear) && perYear > 0 ? perYear : 252.0;
    }

    /// <summary>Whether <see cref="BarsPerYear"/> measured its answer rather than falling back to 252.</summary>
    public static bool IsMeasured(int barCount, DateOnly first, DateOnly last) =>
        barCount >= 2 && YearsBetween(first, last) > 0;
}
