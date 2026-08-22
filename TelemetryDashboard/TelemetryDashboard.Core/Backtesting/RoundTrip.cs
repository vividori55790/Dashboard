using System;

namespace TelemetryDashboard.Core.Backtesting;

/// <summary>A position opened from flat and carried until it was flat again.</summary>
/// <remarks>
/// The unit a win rate can honestly be quoted in. Counting winners per <em>fill</em> instead makes
/// a rule that scales into one position over four buys and exits in one sale look like four trades
/// with one loser, and a rule that does the reverse look like the opposite — so the same behaviour
/// reports a 25 % or a 75 % win rate depending only on how the orders were sliced.
/// </remarks>
public sealed record RoundTrip
{
    /// <summary>Session the position was opened in.</summary>
    public required DateOnly EntryDate { get; init; }

    /// <summary>Session it returned to flat in.</summary>
    public required DateOnly ExitDate { get; init; }

    /// <summary>1 for a long position, -1 for a short one.</summary>
    public required int Direction { get; init; }

    /// <summary>Profit taken out of the position, before the cost of getting in and out.</summary>
    public required double GrossProfit { get; init; }

    /// <summary>Commission and slippage attributed to this position.</summary>
    public required double Costs { get; init; }

    /// <summary>What the position actually left behind.</summary>
    public double NetProfit => GrossProfit - Costs;

    /// <summary>Sessions the position was held for.</summary>
    public int HeldDays => ExitDate.DayNumber - EntryDate.DayNumber;

    /// <summary>Whether the position finished ahead after everything it cost.</summary>
    /// <remarks>
    /// Judged on the net, not the gross. A rule whose winners are thinner than its commission is a
    /// losing rule, and reporting it as a winner is the specific dishonesty this distinction exists
    /// to prevent.
    /// </remarks>
    public bool IsWin => NetProfit > 0;
}
