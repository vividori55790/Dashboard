using System;
using System.Collections.Generic;
using System.Linq;

namespace TelemetryDashboard.Core.Backtesting;

/// <summary>
/// A symbol's bars, in ascending date order, with no date appearing twice.
/// </summary>
/// <remarks>
/// The ordering is enforced here rather than assumed by the engine because both ways of getting it
/// wrong are silent. A file in descending order — which is how several vendors export — runs a
/// strategy backwards through history and reports a plausible equity curve for a trade sequence
/// that could never have happened. A duplicated date double-counts one session's return.
/// <para>
/// This is the same guarantee the replay side of this product needs from a recording, for the same
/// reason: a stage that assumes monotonic time produces confident nonsense when it does not get it.
/// </para>
/// </remarks>
public sealed class BarSeries
{
    private readonly PriceBar[] _bars;

    private BarSeries(string symbol, PriceBar[] bars)
    {
        Symbol = symbol;
        _bars = bars;
    }

    /// <summary>Symbol these bars belong to.</summary>
    public string Symbol { get; }

    /// <summary>Number of bars.</summary>
    public int Count => _bars.Length;

    /// <summary>The i-th bar, oldest first.</summary>
    public PriceBar this[int index] => _bars[index];

    /// <summary>First session in the series.</summary>
    public DateOnly FirstDate => _bars[0].Date;

    /// <summary>Last session in the series.</summary>
    public DateOnly LastDate => _bars[^1].Date;

    /// <summary>The bars, oldest first.</summary>
    public IReadOnlyList<PriceBar> Bars => _bars;

    /// <summary>
    /// Sorts, de-duplicates and validates <paramref name="bars"/> into a series.
    /// </summary>
    /// <remarks>
    /// A duplicate date keeps the last row read, matching how a vendor's own corrections file is
    /// meant to be applied. <paramref name="duplicatesDropped"/> is reported rather than logged
    /// because a file full of duplicates is usually two files concatenated, and that is worth
    /// telling the person who is about to trust the result.
    /// </remarks>
    public static BarSeries Create(string symbol, IEnumerable<PriceBar> bars, out int duplicatesDropped)
    {
        ArgumentNullException.ThrowIfNull(bars);

        var byDate = new SortedDictionary<DateOnly, PriceBar>();
        int seen = 0;
        foreach (PriceBar bar in bars)
        {
            seen++;
            byDate[bar.Date] = bar;
        }

        duplicatesDropped = seen - byDate.Count;
        if (byDate.Count == 0)
        {
            throw new ArgumentException("A series needs at least one bar.", nameof(bars));
        }

        return new BarSeries(
            string.IsNullOrWhiteSpace(symbol) ? "(unnamed)" : symbol.Trim(),
            byDate.Values.ToArray());
    }

    /// <summary>The sub-range whose dates fall inside <paramref name="from"/>..<paramref name="to"/>, inclusive.</summary>
    /// <remarks>
    /// Returns null when the window selects nothing. A caller that got an empty series back would
    /// otherwise have to decide what a backtest over no sessions means, and there is no useful
    /// answer to that — only a message saying the window missed the data.
    /// </remarks>
    public BarSeries? Slice(DateOnly? from, DateOnly? to)
    {
        PriceBar[] kept = _bars
            .Where(b => (from is null || b.Date >= from) && (to is null || b.Date <= to))
            .ToArray();

        return kept.Length == 0 ? null : new BarSeries(Symbol, kept);
    }
}
