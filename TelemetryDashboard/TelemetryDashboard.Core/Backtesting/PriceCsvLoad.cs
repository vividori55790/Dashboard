using System.Collections.Generic;

namespace TelemetryDashboard.Core.Backtesting;

/// <summary>
/// What came out of a price file: the series, or the reason there is none, plus what was discarded.
/// </summary>
/// <remarks>
/// An outcome object rather than an exception because a partially usable file is the normal case,
/// not the exceptional one. A decade of daily bars routinely contains a handful of rows a market
/// never traded — a halted session written as zeros, a placeholder the vendor emits for a date the
/// exchange was shut. Refusing the whole file over four bad rows helps nobody; silently dropping
/// them and reporting a clean run is worse, because the person reading the equity curve has no way
/// to know how much of it was invented.
/// <para>
/// So the discards are carried alongside the data and printed with the result. That is the same
/// rule the ingest side of this product follows for lines it could not parse.
/// </para>
/// </remarks>
public sealed class PriceCsvLoad
{
    /// <summary>The bars, or null when the file could not be used at all.</summary>
    public BarSeries? Series { get; init; }

    /// <summary>Why there is no series, or null when there is one.</summary>
    public string? Error { get; init; }

    /// <summary>Data rows the reader could not turn into a bar at all.</summary>
    public int UnparseableRows { get; init; }

    /// <summary>Rows that parsed but described a session no market could have traded.</summary>
    public int IncoherentRows { get; init; }

    /// <summary>Rows dropped because an earlier row already claimed that date.</summary>
    public int DuplicateDates { get; init; }

    /// <summary>Whether the vendor supplied an adjusted close, or the reader fell back to the close.</summary>
    public bool HasAdjustedClose { get; init; }

    /// <summary>Rows discarded for any reason.</summary>
    public int Discarded => UnparseableRows + IncoherentRows + DuplicateDates;

    /// <summary>One line per kind of discard, empty when the file was clean.</summary>
    public IEnumerable<string> Notes()
    {
        if (UnparseableRows > 0) yield return $"{UnparseableRows} row(s) could not be parsed and were dropped.";
        if (IncoherentRows > 0) yield return $"{IncoherentRows} row(s) described an impossible session and were dropped.";
        if (DuplicateDates > 0) yield return $"{DuplicateDates} duplicate date(s) were dropped, keeping the last read.";
        if (!HasAdjustedClose) yield return "No adjusted-close column: splits and dividends are not accounted for.";
    }
}
