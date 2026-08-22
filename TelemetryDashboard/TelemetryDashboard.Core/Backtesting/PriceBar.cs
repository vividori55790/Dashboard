using System;

namespace TelemetryDashboard.Core.Backtesting;

/// <summary>Which price a run marks its equity against.</summary>
/// <remarks>
/// Not a detail. A close-price series of a dividend-paying share understates the return an investor
/// actually received, and a split shows up in it as a crash that never happened; the adjusted close
/// folds both back in. The two answers differ by tens of percent over a decade, so a backtester
/// that silently picks one is reporting a number whose meaning it never stated.
/// </remarks>
public enum PriceField
{
    /// <summary>The session's closing print, exactly as it traded.</summary>
    Close,

    /// <summary>Close restated for splits and dividends, as the vendor supplied it.</summary>
    AdjustedClose
}

/// <summary>One trading session for one symbol.</summary>
/// <remarks>
/// <see cref="Date"/> is a <see cref="DateOnly"/> rather than a <see cref="DateTime"/> on purpose. A
/// daily bar has no time of day, and giving it one invites a time zone: the same session then lands
/// on different calendar days depending on where the process runs, which is enough to shift every
/// signal in a run by a bar.
/// </remarks>
public sealed record PriceBar
{
    /// <summary>Session date, as the exchange dated it.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>First print of the session. This is the price a backtest can actually fill at.</summary>
    public required double Open { get; init; }

    /// <summary>Highest print of the session.</summary>
    public required double High { get; init; }

    /// <summary>Lowest print of the session.</summary>
    public required double Low { get; init; }

    /// <summary>Last print of the session.</summary>
    public required double Close { get; init; }

    /// <summary>Close restated for splits and dividends, or the close when the vendor gave none.</summary>
    public double AdjustedClose { get; init; }

    /// <summary>Shares traded.</summary>
    public long Volume { get; init; }

    /// <summary>The price this bar reports under <paramref name="field"/>.</summary>
    public double PriceOf(PriceField field) =>
        field == PriceField.AdjustedClose ? AdjustedClose : Close;

    /// <summary>
    /// The open, restated by the same factor the vendor applied to the close.
    /// </summary>
    /// <remarks>
    /// Fills happen at the open and equity is marked at the close, so mixing a raw open with an
    /// adjusted close would book the split as a profit. Vendors publish the adjustment only for the
    /// close, so the factor is recovered from the ratio — which is what the vendor divided by.
    /// </remarks>
    public double OpenOf(PriceField field) =>
        field == PriceField.AdjustedClose && Close > 0 ? Open * (AdjustedClose / Close) : Open;

    /// <summary>
    /// Whether the four prices can describe a real session.
    /// </summary>
    /// <remarks>
    /// Vendor files do carry rows that fail this — a placeholder zero on a halted day, a high below
    /// the low from a bad merge. Filling against one produces a return that no market paid, so the
    /// reader drops it and says how many it dropped rather than quietly averaging it in.
    /// </remarks>
    public bool IsCoherent =>
        double.IsFinite(Open) && double.IsFinite(High) && double.IsFinite(Low) && double.IsFinite(Close)
        && Open > 0 && High > 0 && Low > 0 && Close > 0
        && High >= Low
        && High >= Open && High >= Close
        && Low <= Open && Low <= Close;
}
