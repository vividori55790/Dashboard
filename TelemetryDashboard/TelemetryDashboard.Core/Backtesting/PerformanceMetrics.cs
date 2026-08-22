using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Backtesting;

/// <summary>
/// What an equity curve is worth, read off the whole path rather than its endpoints.
/// </summary>
/// <remarks>
/// Total return alone is the number that sells a strategy and the number that tells you least. Two
/// runs ending a decade up 180 % are not the same investment if one of them spent 2022 down 60 %,
/// and nobody holds through the second on purpose. The drawdown and the ratios are here so the
/// question "would a person have stayed in this" has an answer.
/// </remarks>
public sealed record PerformanceMetrics
{
    /// <summary>Equity at the end divided by equity at the start, minus one.</summary>
    public required double TotalReturn { get; init; }

    /// <summary>The constant annual rate that would have produced <see cref="TotalReturn"/>.</summary>
    /// <remarks>NaN when the account was wiped out, because no rate compounds to a negative balance.</remarks>
    public required double Cagr { get; init; }

    /// <summary>Deepest peak-to-trough fall in equity, as a fraction of the peak.</summary>
    public required double MaxDrawdown { get; init; }

    /// <summary>Session the deepest drawdown reached its low.</summary>
    public required DateOnly MaxDrawdownDate { get; init; }

    /// <summary>Standard deviation of returns, annualised.</summary>
    public required double Volatility { get; init; }

    /// <summary>Return per unit of volatility, annualised, against a zero risk-free rate.</summary>
    /// <remarks>
    /// The risk-free rate is zero and is not configurable, which overstates this figure by roughly
    /// the cash rate of the period. Said here because a Sharpe quoted without its risk-free
    /// assumption is not a comparable number, and every backtester that omits the assumption is
    /// quietly using this one.
    /// </remarks>
    public required double Sharpe { get; init; }

    /// <summary>Sharpe counting only downside deviation, so upside volatility is not penalised.</summary>
    public required double Sortino { get; init; }

    /// <summary>Bars per year this series implied, used for every annualised figure above.</summary>
    public required double BarsPerYear { get; init; }

    /// <summary>Whether <see cref="BarsPerYear"/> was measured or fell back to the 252 convention.</summary>
    public required bool BarsPerYearMeasured { get; init; }

    /// <summary>Whether equity reached zero or below at any point.</summary>
    public required bool Ruined { get; init; }

    /// <summary>Reads the metrics off an equity curve.</summary>
    public static PerformanceMetrics From(IReadOnlyList<EquityPoint> curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        if (curve.Count == 0) throw new ArgumentException("An empty curve has no performance.", nameof(curve));

        double start = curve[0].Equity;
        double end = curve[^1].Equity;
        bool ruined = false;

        double peak = start, maxDrawdown = 0;
        DateOnly troughDate = curve[0].Date;
        var returns = new List<double>(curve.Count);

        for (int i = 0; i < curve.Count; i++)
        {
            double equity = curve[i].Equity;
            if (equity <= 0) ruined = true;

            if (equity > peak) peak = equity;
            if (peak > 0)
            {
                double drawdown = (peak - equity) / peak;
                if (drawdown > maxDrawdown)
                {
                    maxDrawdown = drawdown;
                    troughDate = curve[i].Date;
                }
            }

            // A step from a non-positive balance has no meaningful return, and including one would
            // put a sign flip into the series every ratio below is computed from.
            if (i > 0 && curve[i - 1].Equity > 0) returns.Add(equity / curve[i - 1].Equity - 1.0);
        }

        double perYear = TradingCalendar.BarsPerYear(curve.Count, curve[0].Date, curve[^1].Date);
        double years = TradingCalendar.YearsBetween(curve[0].Date, curve[^1].Date);
        double scale = Math.Sqrt(perYear);
        (double mean, double deviation, double downside) = Moments(returns);

        return new PerformanceMetrics
        {
            TotalReturn = start > 0 ? end / start - 1.0 : double.NaN,
            Cagr = start > 0 && end > 0 && years > 0 ? Math.Pow(end / start, 1.0 / years) - 1.0 : double.NaN,
            MaxDrawdown = maxDrawdown,
            MaxDrawdownDate = troughDate,
            Volatility = deviation * scale,
            Sharpe = deviation > 0 ? mean / deviation * scale : double.NaN,
            Sortino = downside > 0 ? mean / downside * scale : double.NaN,
            BarsPerYear = perYear,
            BarsPerYearMeasured = TradingCalendar.IsMeasured(curve.Count, curve[0].Date, curve[^1].Date),
            Ruined = ruined
        };
    }

    /// <summary>Mean, sample standard deviation, and the deviation of the negative half.</summary>
    /// <remarks>
    /// The downside deviation divides by the count of <em>all</em> returns rather than of the
    /// negative ones. That is the standard definition and it is not an oversight: dividing by the
    /// negative count would let a strategy improve its Sortino by having fewer, larger losses.
    /// </remarks>
    private static (double Mean, double Deviation, double Downside) Moments(List<double> returns)
    {
        if (returns.Count < 2) return (0, 0, 0);

        double sum = 0;
        foreach (double r in returns) sum += r;
        double mean = sum / returns.Count;

        double squares = 0, negativeSquares = 0;
        foreach (double r in returns)
        {
            double d = r - mean;
            squares += d * d;
            if (r < 0) negativeSquares += r * r;
        }

        return (mean,
            Math.Sqrt(squares / (returns.Count - 1)),
            Math.Sqrt(negativeSquares / returns.Count));
    }
}
