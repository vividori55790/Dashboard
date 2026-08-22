using System;

namespace TelemetryDashboard.Core.Backtesting;

/// <summary>
/// Holds a position, its average cost, and realises profit when part of it is closed.
/// </summary>
/// <remarks>
/// Separated from the engine because the case that gets this wrong is the one that reads correctly:
/// a trade large enough to cross through zero both closes the old position and opens an opposite
/// one, and the profit realised belongs only to the part that closed. Folded into the engine's loop
/// this is three lines that look obviously right and are not, and the error shows up only in the
/// win rate — a statistic nobody checks against an independent calculation.
/// </remarks>
public sealed class PositionLedger
{
    /// <summary>Shares held; negative when short.</summary>
    public double Shares { get; private set; }

    /// <summary>Average price paid for what is currently held, or 0 when flat.</summary>
    public double AverageCost { get; private set; }

    /// <summary>Profit taken out of closed positions so far.</summary>
    public double RealisedProfit { get; private set; }

    /// <summary>Applies a trade and returns the profit it realised.</summary>
    /// <param name="delta">Shares bought (positive) or sold (negative).</param>
    /// <param name="price">Price the trade filled at, slippage already applied.</param>
    public double Apply(double delta, double price)
    {
        if (delta == 0 || !double.IsFinite(delta)) return 0.0;

        // Same direction, or opening from flat: nothing closes, so nothing is realised and the
        // cost basis becomes the weighted average of what was held and what was just added.
        if (Shares == 0 || Math.Sign(Shares) == Math.Sign(delta))
        {
            double total = Shares + delta;
            AverageCost = (AverageCost * Shares + price * delta) / total;
            Shares = total;
            return 0.0;
        }

        double held = Shares;
        double closing = Math.Min(Math.Abs(held), Math.Abs(delta));
        double realised = closing * (price - AverageCost) * Math.Sign(held);
        RealisedProfit += realised;

        Shares = held + delta;

        if (Shares == 0)
        {
            AverageCost = 0.0;
        }
        else if (Math.Sign(Shares) != Math.Sign(held))
        {
            // Crossed through zero. The part beyond the close is a new position, and it was opened
            // at this price -- carrying the old average across would attribute the previous
            // position's cost to shares bought in the opposite direction.
            AverageCost = price;
        }

        return realised;
    }

    /// <summary>Value of the holding at <paramref name="price"/>.</summary>
    public double MarketValue(double price) => Shares * price;

    /// <summary>Forgets the position, for a fresh run.</summary>
    public void Reset()
    {
        Shares = 0;
        AverageCost = 0;
        RealisedProfit = 0;
    }
}
