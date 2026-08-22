using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TelemetryDashboard.Core.Backtesting;

/// <summary>
/// Which column holds what, resolved once from the header, and how to read a row through it.
/// </summary>
/// <remarks>
/// Column positions come from the header rather than being fixed, because the vendor exports
/// differ: Yahoo writes <c>Date,Open,High,Low,Close,Adj Close,Volume</c> and Stooq omits the
/// adjusted close. A reader that counted columns would take Stooq's volume as an adjusted close and
/// mark a decade of equity against share counts — a wrong answer that never once looks like an
/// error.
/// <para>
/// Every number is parsed with <see cref="CultureInfo.InvariantCulture"/>. Not decoration: on a
/// machine whose locale writes decimals with a comma, culture-sensitive parsing turns 218.53 into
/// 21853 and succeeds.
/// </para>
/// </remarks>
internal sealed class PriceCsvColumns
{
    private static readonly string[] DateNames = { "date", "timestamp", "time" };
    private static readonly string[] AdjustedNames = { "adj close", "adj_close", "adjclose", "adjusted close" };
    private static readonly string[] Required = { "Open", "High", "Low", "Close" };

    private readonly Dictionary<string, int> _index;
    private readonly int _date;
    private readonly int _adjusted;

    private PriceCsvColumns(Dictionary<string, int> index, int date, int adjusted)
    {
        _index = index;
        _date = date;
        _adjusted = adjusted;
    }

    /// <summary>Whether the vendor supplied an adjusted close at all.</summary>
    public bool HasAdjustedClose => _adjusted >= 0;

    /// <summary>Consumes rows until the header, or explains why there is not one.</summary>
    public static bool TryRead(IEnumerator<string> rows, out PriceCsvColumns? columns, out string? error)
    {
        columns = null;

        while (rows.MoveNext())
        {
            if (string.IsNullOrWhiteSpace(rows.Current)) continue;

            // The byte-order mark a spreadsheet export leaves on the first cell would otherwise
            // make the first column name unmatchable, and the file read as one with no header.
            string[] names = rows.Current.TrimStart('﻿').Split(',');
            var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < names.Length; i++) found[names[i].Trim()] = i;

            int date = IndexOfAny(found, DateNames);
            if (date < 0)
            {
                error = "first non-empty line is not a header: no Date column. "
                      + "Expected a vendor daily export, e.g. Date,Open,High,Low,Close,Adj Close,Volume.";
                return false;
            }

            string[] missing = Required.Where(n => !found.ContainsKey(n)).ToArray();
            if (missing.Length > 0)
            {
                error = $"header is missing required column(s): {string.Join(", ", missing)}.";
                return false;
            }

            columns = new PriceCsvColumns(found, date, IndexOfAny(found, AdjustedNames));
            error = null;
            return true;
        }

        error = "the file is empty.";
        return false;
    }

    /// <summary>Reads one data row, or null when it does not describe a session.</summary>
    public PriceBar? ParseRow(string[] cells)
    {
        if (!TryDate(cells, out DateOnly date)) return null;
        if (!TryNumber(cells, _index["Open"], out double open)) return null;
        if (!TryNumber(cells, _index["High"], out double high)) return null;
        if (!TryNumber(cells, _index["Low"], out double low)) return null;
        if (!TryNumber(cells, _index["Close"], out double close)) return null;

        double adjusted = HasAdjustedClose && TryNumber(cells, _adjusted, out double a) ? a : close;
        long volume = _index.TryGetValue("Volume", out int v) && TryNumber(cells, v, out double traded)
            ? (long)traded
            : 0L;

        return new PriceBar
        {
            Date = date,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            AdjustedClose = adjusted,
            Volume = volume
        };
    }

    private bool TryDate(string[] cells, out DateOnly date)
    {
        date = default;
        if (_date >= cells.Length) return false;

        string text = cells[_date].Trim().Trim('"');

        // A vendor that stamps a time of day is still describing one session; the calendar date is
        // the whole of the information and the clock part is an artefact of the export.
        int splitAt = text.IndexOfAny(new[] { ' ', 'T' });
        if (splitAt > 0) text = text[..splitAt];

        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out date)
            || DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryNumber(string[] cells, int index, out double value)
    {
        value = 0;
        if (index < 0 || index >= cells.Length) return false;

        string text = cells[index].Trim().Trim('"');

        // Vendors write an unknown as "null", "N/A" or nothing at all. Each of those parses to zero
        // under a lenient reader, and a zero price is a total loss the market never delivered.
        return text.Length != 0
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static int IndexOfAny(Dictionary<string, int> columns, string[] names)
    {
        foreach (string name in names)
        {
            if (columns.TryGetValue(name, out int index)) return index;
        }
        return -1;
    }
}
