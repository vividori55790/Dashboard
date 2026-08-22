using System;

namespace TelemetryDashboard.Core.Backtesting;

/// <summary>One executed trade, as the simulator filled it.</summary>
/// <remarks>
/// Every fill is kept, not just a count, because a total is not evidence. Two runs reporting
/// "31 trades" can be a rule that traded on 31 genuine signals and a rule that traded 31 times in
/// one fortnight and then never again, and only the list distinguishes them. The report prints the
/// first and last few; the rest exist so the result can be exported and checked.
/// </remarks>
public sealed record TradeFill
{
    /// <summary>Session the trade filled in.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Shares bought (positive) or sold (negative).</summary>
    public required double Shares { get; init; }

    /// <summary>Price the trade filled at, with slippage already moved against it.</summary>
    public required double Price { get; init; }

    /// <summary>Price before slippage, as the bar reported it.</summary>
    public required double ReferencePrice { get; init; }

    /// <summary>Commission charged on this trade.</summary>
    public required double Commission { get; init; }

    /// <summary>Profit this trade took out of a position it closed; zero when it only opened one.</summary>
    public required double RealisedProfit { get; init; }

    /// <summary>Account equity immediately after the fill.</summary>
    public required double EquityAfter { get; init; }

    /// <summary>Value traded, before costs.</summary>
    public double Notional => Math.Abs(Shares) * Price;

    /// <summary>What slippage cost on this trade, separately from commission.</summary>
    public double SlippageCost => Math.Abs(Shares) * Math.Abs(Price - ReferencePrice);

    /// <summary>Everything this trade cost that a frictionless model would have reported as zero.</summary>
    public double TotalCost => Commission + SlippageCost;

    /// <summary>Whether this trade increased exposure rather than reducing it.</summary>
    public bool IsBuy => Shares > 0;
}
