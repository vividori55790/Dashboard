using System;
using System.Collections.Generic;
using System.IO;

namespace TelemetryDashboard.Core.Backtesting;

/// <summary>
/// Reads a daily OHLCV file as the common market-data vendors export it.
/// </summary>
/// <remarks>
/// The loop only. Which column holds what, and how a cell becomes a number, is
/// <see cref="PriceCsvColumns"/>; keeping them apart is what stops this file from being one
/// procedure that resolves a header, parses a date, tolerates a locale and counts discards.
/// </remarks>
public static class PriceCsvReader
{
    /// <summary>Reads <paramref name="path"/>, taking the symbol from the file name.</summary>
    public static PriceCsvLoad ReadFile(string path)
    {
        if (!File.Exists(path)) return new PriceCsvLoad { Error = $"no such file: {path}" };

        try
        {
            return Read(File.ReadLines(path), Path.GetFileNameWithoutExtension(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PriceCsvLoad { Error = $"cannot read {path}: {ex.Message}" };
        }
    }

    /// <summary>Reads already-open lines, so a caller can feed a stream or a test can feed literals.</summary>
    public static PriceCsvLoad Read(IEnumerable<string> lines, string symbol)
    {
        ArgumentNullException.ThrowIfNull(lines);

        using IEnumerator<string> rows = lines.GetEnumerator();
        if (!PriceCsvColumns.TryRead(rows, out PriceCsvColumns? columns, out string? headerError))
        {
            return new PriceCsvLoad { Error = headerError };
        }

        var bars = new List<PriceBar>();
        int unparseable = 0, incoherent = 0;

        while (rows.MoveNext())
        {
            if (string.IsNullOrWhiteSpace(rows.Current)) continue;

            PriceBar? bar = columns!.ParseRow(rows.Current.Split(','));
            if (bar is null) { unparseable++; continue; }

            // Counted apart from an unreadable row on purpose. A file whose numbers all parse and
            // whose sessions are impossible is a different problem -- a bad merge, a placeholder
            // convention -- from one that is not the format it was taken for.
            if (!bar.IsCoherent) { incoherent++; continue; }

            bars.Add(bar);
        }

        if (bars.Count == 0)
        {
            return new PriceCsvLoad
            {
                Error = "no usable rows: the file has a header but nothing that parses as a session.",
                UnparseableRows = unparseable,
                IncoherentRows = incoherent
            };
        }

        return new PriceCsvLoad
        {
            Series = BarSeries.Create(symbol, bars, out int duplicates),
            UnparseableRows = unparseable,
            IncoherentRows = incoherent,
            DuplicateDates = duplicates,
            HasAdjustedClose = columns!.HasAdjustedClose
        };
    }
}
