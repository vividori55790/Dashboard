using System;

namespace TelemetryDashboard.Core.Backtesting;

/// <summary>The account marked to market at one session's close.</summary>
/// <remarks>
/// The whole equity curve is retained rather than a running maximum and a final figure, because
/// every risk statistic worth reporting is a property of the path and not of the endpoints. Two
/// accounts that both end the decade up 180 % are not the same investment if one of them was down
/// 60 % in the middle, and no summary computed from the first and last values can tell them apart.
/// <para>
/// Sized for the series a person actually backtests: a decade of daily bars is 2,500 of these, and
/// even a century of them is a rounding error against the price data itself.
/// </para>
/// </remarks>
public readonly record struct EquityPoint
{
    /// <summary>Session this mark belongs to.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Cash plus the market value of the position, at this session's close.</summary>
    public required double Equity { get; init; }

    /// <summary>Fraction of equity held in the symbol: 1 fully long, 0 flat, -1 fully short.</summary>
    public required double Weight { get; init; }

    /// <summary>Price the mark used, under the run's chosen field.</summary>
    public required double Price { get; init; }
}
