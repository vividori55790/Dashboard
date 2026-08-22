using System;

namespace TelemetryDashboard.Core.Backtesting;

/// <summary>
/// The account and the friction a run is simulated under.
/// </summary>
/// <remarks>
/// Costs default to non-zero, which is the whole point of this type. A frictionless backtest is the
/// second-favourite way to make a losing strategy look profitable — the first being look-ahead —
/// and it is worse than look-ahead in one respect: it hurts most precisely the strategies that
/// trade most, so it systematically flatters the busiest and least workable rules while barely
/// touching the ones that would have been fine anyway. A default of zero would make every quick
/// experiment wrong in that direction.
/// </remarks>
public sealed record BacktestSettings
{
    /// <summary>Cash the account starts with, in the symbol's currency.</summary>
    public double StartingCash { get; init; } = 10_000.0;

    /// <summary>
    /// Commission charged on the value traded, in basis points (1 bp = 0.01 %).
    /// </summary>
    /// <remarks>
    /// A rate rather than a per-trade fee because the fills here are fractional and weight-based; a
    /// flat fee would depend on a lot size this model does not have. Five basis points is around
    /// where a retail account lands once the spread is in the slippage term below.
    /// </remarks>
    public double CommissionBps { get; init; } = 5.0;

    /// <summary>
    /// How far the fill price moves against the order, in basis points of the reference price.
    /// </summary>
    /// <remarks>
    /// Stands in for the half-spread and for the market moving while an order is worked. Applied
    /// against the direction of the trade in both directions, so it can never help.
    /// </remarks>
    public double SlippageBps { get; init; } = 2.0;

    /// <summary>Price the run marks equity against.</summary>
    public PriceField Field { get; init; } = PriceField.AdjustedClose;

    /// <summary>
    /// Smallest trade worth placing, as a fraction of equity.
    /// </summary>
    /// <remarks>
    /// Without a floor, a strategy holding a constant weight still trades every single bar: equity
    /// moves with the price, so the target value moves with it, and the position is a few
    /// ten-thousandths off target every morning. Those corrections are individually invisible and
    /// collectively pay commission on the whole account hundreds of times over a decade.
    /// </remarks>
    public double MinimumTradeFraction { get; init; } = 0.005;

    /// <summary>Multiplier form of <see cref="CommissionBps"/>.</summary>
    public double CommissionRate => CommissionBps / 10_000.0;

    /// <summary>Multiplier form of <see cref="SlippageBps"/>.</summary>
    public double SlippageRate => SlippageBps / 10_000.0;

    /// <summary>Why these settings cannot be run, or null when they can.</summary>
    public string? Validate()
    {
        if (!(StartingCash > 0)) return "starting cash must be positive.";
        if (CommissionBps < 0) return "commission cannot be negative.";
        if (SlippageBps < 0) return "slippage cannot be negative.";
        if (MinimumTradeFraction < 0 || MinimumTradeFraction >= 1)
        {
            return "the minimum trade fraction must be at least 0 and below 1.";
        }
        if (!double.IsFinite(StartingCash)) return "starting cash must be a finite number.";

        return null;
    }
}
