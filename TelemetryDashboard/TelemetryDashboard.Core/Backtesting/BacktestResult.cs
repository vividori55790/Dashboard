using System;
using System.Collections.Generic;
using System.Linq;

namespace TelemetryDashboard.Core.Backtesting;

/// <summary>Everything one run produced, and everything about it worth doubting.</summary>
public sealed class BacktestResult
{
    private PerformanceMetrics? _metrics;

    /// <summary>Symbol the run was over.</summary>
    public required string Symbol { get; init; }

    /// <summary>Strategy as it named itself, parameters included.</summary>
    public required string StrategyName { get; init; }

    /// <summary>Account and friction the run used.</summary>
    public required BacktestSettings Settings { get; init; }

    /// <summary>Equity marked at every session's close.</summary>
    public required IReadOnlyList<EquityPoint> Curve { get; init; }

    /// <summary>Every trade that filled.</summary>
    public required IReadOnlyList<TradeFill> Fills { get; init; }

    /// <summary>Positions opened and closed.</summary>
    public required IReadOnlyList<RoundTrip> RoundTrips { get; init; }

    /// <summary>Bars the strategy spent before its answer meant anything.</summary>
    public required int WarmUpBars { get; init; }

    /// <summary>Bars a position was held on.</summary>
    public required int BarsHoldingPosition { get; init; }

    /// <summary>Commission paid across the run.</summary>
    public required double CommissionPaid { get; init; }

    /// <summary>Slippage paid across the run.</summary>
    public required double SlippagePaid { get; init; }

    /// <summary>Whether the run finished still holding.</summary>
    /// <remarks>
    /// Reported because the final position is marked at the last close and never sold, so its cost
    /// of exit is not in any figure here. A rule that ends up holding is being credited with a
    /// price it did not have to trade out of.
    /// </remarks>
    public required bool EndedWithOpenPosition { get; init; }

    /// <summary>
    /// The decision the last bar produced, which no session existed to fill.
    /// </summary>
    /// <remarks>
    /// Kept rather than dropped because it is the one output of a backtest that is about tomorrow
    /// instead of about the past: it is what the rule says to hold now.
    /// </remarks>
    public required double? UnexecutedFinalSignal { get; init; }

    /// <summary>Metrics read off <see cref="Curve"/>.</summary>
    public PerformanceMetrics Metrics => _metrics ??= PerformanceMetrics.From(Curve);

    /// <summary>First session of the run.</summary>
    public DateOnly FirstDate => Curve[0].Date;

    /// <summary>Last session of the run.</summary>
    public DateOnly LastDate => Curve[^1].Date;

    /// <summary>Equity at the last close.</summary>
    public double FinalEquity => Curve[^1].Equity;

    /// <summary>Commission plus slippage, as a fraction of the starting account.</summary>
    public double CostDrag => (CommissionPaid + SlippagePaid) / Settings.StartingCash;

    /// <summary>Fraction of sessions a position was held on.</summary>
    public double Exposure => Curve.Count == 0 ? 0 : (double)BarsHoldingPosition / Curve.Count;

    /// <summary>Round trips that finished ahead, as a fraction of those that finished.</summary>
    /// <remarks>NaN when nothing closed: a rate over zero trades is not zero, it is unknown.</remarks>
    public double WinRate => RoundTrips.Count == 0
        ? double.NaN
        : (double)RoundTrips.Count(t => t.IsWin) / RoundTrips.Count;

    /// <summary>Mean net profit of a closed round trip, or NaN when none closed.</summary>
    public double AverageTrip => RoundTrips.Count == 0
        ? double.NaN
        : RoundTrips.Average(t => t.NetProfit);
}
