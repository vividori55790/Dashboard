using System;
using TelemetryDashboard.Core.Analytics;

namespace TelemetryDashboard.Core.Backtesting.Strategies;

/// <summary>
/// Buys the symbol when it sits far below its own recent mean, and lets go when it comes back.
/// </summary>
/// <remarks>
/// This is the telemetry side of the product pointed at a price. <see cref="RollingChannelStatistics"/>
/// is the class that decides a converter rail is behaving unlike itself, and the question here is
/// the same one — how far is this reading from where this channel has been? — asked of a close
/// instead of a bus voltage. Its baseline deliberately excludes the newest sample, so a large move
/// cannot inflate the deviation it is measured against and partially mask itself; that property is
/// why the class is reused rather than a mean recomputed here.
/// <para>
/// The entry and exit thresholds differ for the reason <see cref="AnomalyAlarmGate"/> keeps its two
/// apart: a single threshold makes a series hovering on it flip state on consecutive bars, and here
/// each flip is a real trade paying real commission. The gate calls that a flickering alarm; a
/// backtest calls it churn, and it is the same defect.
/// </para>
/// <para>
/// It is a rule for showing how the pieces fit together, not a recommendation. Mean reversion is
/// the family of strategy that looks best in a backtest and survives contact with a trend worst:
/// buying something because it fell is indistinguishable, in the window before it recovers, from
/// buying something because it is going to keep falling.
/// </para>
/// </remarks>
public sealed class MeanReversionStrategy : IBarStrategy
{
    private readonly RollingChannelStatistics _statistics;
    private readonly double _entryZ;
    private readonly double _exitZ;
    private readonly bool _allowShort;
    private double _weight;

    /// <summary>Builds the rule from its lookback and its two thresholds, in standard deviations.</summary>
    public MeanReversionStrategy(int window = 20, double entryZ = 2.0, double exitZ = 0.5, bool allowShort = false)
    {
        if (entryZ <= exitZ)
        {
            throw new ArgumentOutOfRangeException(nameof(entryZ),
                "The entry threshold must exceed the exit one, or the rule opens and closes on the same bar.");
        }

        _statistics = new RollingChannelStatistics(window);
        _entryZ = entryZ;
        _exitZ = exitZ;
        _allowShort = allowShort;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Plain ASCII. The sigma character was the obvious way to write this and it printed as two
    /// replacement bytes on a Windows console under a non-UTF-8 code page -- the name of the rule,
    /// mangled, at the top of its own report. The report is product output, not a comment.
    /// </remarks>
    public string Name =>
        $"mean-reversion({_statistics.Capacity}, in {_entryZ:0.##}z, out {_exitZ:0.##}z"
        + (_allowShort ? ", short)" : ")");

    /// <inheritdoc />
    public int WarmUpBars => _statistics.Capacity;

    /// <inheritdoc />
    public void Reset()
    {
        _statistics.Clear();
        _weight = 0;
    }

    /// <inheritdoc />
    public double? Decide(StrategyContext context)
    {
        double price = context.Price;

        // Added before it is measured, so the baseline is every earlier bar in the window and this
        // close is scored against them. Measuring first would score it against a window that has
        // not yet been told about the previous bar, which lags the whole rule by one session.
        _statistics.Add(price);
        if (_statistics.Count < _statistics.Capacity) return null;

        double sigma = _statistics.ZScoreOf(price);

        // A window whose prices never moved has no standard deviation, and ZScoreOf answers 0 for
        // it rather than dividing by nothing. That reads as "sitting on the mean", which is what a
        // flat series is, so no position is opened -- the honest answer for a symbol carrying no
        // information about what an excursion of it would look like.
        if (_weight == 0)
        {
            if (sigma < _entryZ) return null;
            double below = _statistics.Mean - price;
            if (below > 0) _weight = 1.0;
            else if (_allowShort) _weight = -1.0;
            else return null;

            return _weight;
        }

        if (sigma > _exitZ) return null;

        _weight = 0;
        return 0.0;
    }
}
