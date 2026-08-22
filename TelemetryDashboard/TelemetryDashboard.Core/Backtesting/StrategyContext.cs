using System;

namespace TelemetryDashboard.Core.Backtesting;

/// <summary>
/// Everything a strategy is allowed to know when a bar closes: this bar and every bar before it.
/// </summary>
/// <remarks>
/// The look-ahead guard is the shape of this type, not a rule someone has to remember. A strategy
/// is handed a series and an index and can only index backwards from it, so a strategy that peeks
/// at tomorrow's close does not produce an optimistic backtest — it fails to compile.
/// <para>
/// This matters more than any other correctness property here. Look-ahead is the defect that makes
/// a backtest wrong in the flattering direction: it does not crash, it does not look odd, it simply
/// reports a strategy as profitable when it is not, and it is invisible in the output. Every other
/// bug in a backtester announces itself; this one recruits you.
/// </para>
/// <para>
/// A struct so the engine can build one per bar without allocating, and readonly so a strategy
/// cannot advance it and read past its own bar.
/// </para>
/// </remarks>
public readonly struct StrategyContext
{
    private readonly BarSeries _series;

    /// <summary>Builds the view of <paramref name="series"/> as of <paramref name="index"/>.</summary>
    public StrategyContext(BarSeries series, int index, PriceField field, double currentWeight)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (index < 0 || index >= series.Count) throw new ArgumentOutOfRangeException(nameof(index));

        _series = series;
        Index = index;
        Field = field;
        CurrentWeight = currentWeight;
    }

    /// <summary>Position of the bar that just closed, counted from the start of the series.</summary>
    public int Index { get; }

    /// <summary>Which price the run is marked against.</summary>
    public PriceField Field { get; }

    /// <summary>
    /// Fraction of equity currently held in the symbol: 1 fully long, 0 flat, -1 fully short.
    /// </summary>
    /// <remarks>
    /// Given because a strategy that cannot see its own position has to infer it from its own past
    /// answers, and the two disagree the moment a trade is skipped for being too small to place.
    /// </remarks>
    public double CurrentWeight { get; }

    /// <summary>Bars available so far, including this one.</summary>
    public int Count => Index + 1;

    /// <summary>The bar that just closed.</summary>
    public PriceBar Current => _series[Index];

    /// <summary>Price of the bar that just closed, under <see cref="Field"/>.</summary>
    public double Price => Current.PriceOf(Field);

    /// <summary>The bar <paramref name="barsAgo"/> sessions before this one; 0 is this one.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for a negative <paramref name="barsAgo"/> — which is a request for the future — and
    /// for one reaching before the first session held.
    /// </exception>
    public PriceBar Ago(int barsAgo)
    {
        if (barsAgo < 0) throw new ArgumentOutOfRangeException(nameof(barsAgo), "A strategy cannot read a later bar.");
        if (barsAgo > Index) throw new ArgumentOutOfRangeException(nameof(barsAgo), "That is before the series starts.");

        return _series[Index - barsAgo];
    }

    /// <summary>Price <paramref name="barsAgo"/> sessions before this one, under <see cref="Field"/>.</summary>
    public double PriceAgo(int barsAgo) => Ago(barsAgo).PriceOf(Field);
}
