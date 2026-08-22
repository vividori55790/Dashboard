using System;

namespace TelemetryDashboard.Core.Backtesting.Strategies;

/// <summary>
/// Holds the symbol while a short average is above a long one, and steps aside when it is not.
/// </summary>
/// <remarks>
/// The canonical trend-following rule, included because it is the one everybody tries first and
/// because what it actually does is worth seeing measured: it trades rarely, it gives up the first
/// part of every rise and the last part of every fall, and whether that is worth paying for depends
/// entirely on the symbol and the period. This makes that argument with numbers instead of opinion.
/// </remarks>
public sealed class MovingAverageCrossStrategy : IBarStrategy
{
    private readonly MovingAverage _fast;
    private readonly MovingAverage _slow;
    private readonly bool _allowShort;

    /// <summary>Builds the rule from its two periods, in bars.</summary>
    /// <param name="fastPeriod">Short average. Must be shorter than <paramref name="slowPeriod"/>.</param>
    /// <param name="slowPeriod">Long average.</param>
    /// <param name="allowShort">
    /// Whether a downtrend is shorted rather than sat out. Off by default: going short is a
    /// materially different risk — losses are unbounded above — and it should be asked for.
    /// </param>
    public MovingAverageCrossStrategy(int fastPeriod, int slowPeriod, bool allowShort = false)
    {
        if (fastPeriod < 1) throw new ArgumentOutOfRangeException(nameof(fastPeriod));
        if (slowPeriod <= fastPeriod)
        {
            // Equal periods produce two identical averages that never cross, so the rule holds
            // whatever its first bar happened to say, forever. That is not a degenerate edge case
            // worth tolerating -- it is a run that reports a result for a strategy nobody wrote.
            throw new ArgumentOutOfRangeException(nameof(slowPeriod),
                "The slow period must be longer than the fast one, or the averages never cross.");
        }

        _fast = new MovingAverage(fastPeriod);
        _slow = new MovingAverage(slowPeriod);
        _allowShort = allowShort;
    }

    /// <inheritdoc />
    public string Name => $"sma-cross({_fast.Period}/{_slow.Period}{(_allowShort ? ",short" : string.Empty)})";

    /// <inheritdoc />
    public int WarmUpBars => _slow.Period;

    /// <inheritdoc />
    public void Reset()
    {
        _fast.Reset();
        _slow.Reset();
    }

    /// <inheritdoc />
    public double? Decide(StrategyContext context)
    {
        double price = context.Price;
        _fast.Add(price);
        _slow.Add(price);

        // Until the long window spans its whole period its average is over however many bars have
        // arrived, which early in a series is a short average by another name -- so the two would
        // cross on nothing but their differing warm-up lengths, and the first trade of every run
        // would be an artefact of where the file happens to start.
        if (!_slow.IsReady) return null;

        if (_fast.Value > _slow.Value) return 1.0;
        return _allowShort ? -1.0 : 0.0;
    }
}
