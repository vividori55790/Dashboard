using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Backtesting;

/// <summary>
/// Turns a stream of fills into completed round trips.
/// </summary>
/// <remarks>
/// Its own class because of one case: a trade large enough to carry the position through zero ends
/// one round trip and begins another at the same instant, and its cost belongs partly to each. That
/// is three or four lines that are easy to write, easy to believe, and wrong in a way that only
/// ever shows up as a slightly optimistic win rate.
/// </remarks>
public sealed class RoundTripTracker
{
    private readonly List<RoundTrip> _closed = new();
    private DateOnly _entry;
    private int _direction;
    private double _gross;
    private double _cost;

    /// <summary>Positions that were opened and closed.</summary>
    public IReadOnlyList<RoundTrip> Closed => _closed;

    /// <summary>Whether a position is still open at the end of the run.</summary>
    public bool HasOpenTrip { get; private set; }

    /// <summary>Folds one fill in.</summary>
    /// <param name="date">Session the fill happened in.</param>
    /// <param name="before">Shares held before it.</param>
    /// <param name="after">Shares held after it.</param>
    /// <param name="realised">Profit the fill took out of a closed portion.</param>
    /// <param name="cost">Commission plus slippage the fill paid.</param>
    public void Record(DateOnly date, double before, double after, double realised, double cost)
    {
        if (before == 0 && after == 0) return;

        if (before == 0)
        {
            Open(date, Math.Sign(after), cost);
            return;
        }

        if (after == 0)
        {
            Accumulate(realised, cost);
            Close(date);
            return;
        }

        if (Math.Sign(before) == Math.Sign(after))
        {
            Accumulate(realised, cost);
            return;
        }

        // Through zero. The cost is split by how many shares each side of the flip accounted for,
        // which is the only division that does not arbitrarily favour one of the two trips.
        double closedShare = Math.Abs(before) / (Math.Abs(before) + Math.Abs(after));
        Accumulate(realised, cost * closedShare);
        Close(date);
        Open(date, Math.Sign(after), cost * (1.0 - closedShare));
    }

    /// <summary>Forgets everything, for a fresh run.</summary>
    public void Reset()
    {
        _closed.Clear();
        HasOpenTrip = false;
        _gross = 0;
        _cost = 0;
    }

    private void Open(DateOnly date, int direction, double cost)
    {
        HasOpenTrip = true;
        _entry = date;
        _direction = direction;
        _gross = 0;
        _cost = cost;
    }

    private void Accumulate(double realised, double cost)
    {
        _gross += realised;
        _cost += cost;
    }

    private void Close(DateOnly date)
    {
        _closed.Add(new RoundTrip
        {
            EntryDate = _entry,
            ExitDate = date,
            Direction = _direction,
            GrossProfit = _gross,
            Costs = _cost
        });

        HasOpenTrip = false;
        _gross = 0;
        _cost = 0;
    }
}
