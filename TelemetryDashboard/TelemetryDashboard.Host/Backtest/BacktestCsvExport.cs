using System;
using System.Globalization;
using System.IO;
using System.Text;
using TelemetryDashboard.Core.Backtesting;

namespace TelemetryDashboard.Host.Backtest;

/// <summary>
/// Writes the equity curve and the fills out, so a result can be checked rather than believed.
/// </summary>
/// <remarks>
/// The reason this exists is not convenience. A backtester's summary is a claim, and the only way
/// to test a claim is to recompute it somewhere else — the curve loaded into a spreadsheet gives
/// the same drawdown or it does not. A tool that reports only its own conclusions is asking to be
/// trusted on the strength of having been written confidently.
/// <para>
/// Both files are ISO dates and invariant decimals, which is what every other CSV this product
/// writes uses, and what a reader in another locale can parse without guessing.
/// </para>
/// </remarks>
public static class BacktestCsvExport
{
    /// <summary>Writes one row per session: the mark, the position and the price it was marked at.</summary>
    public static void WriteEquity(string path, BacktestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var text = new StringBuilder("Date,Equity,Weight,Price\n");
        foreach (EquityPoint point in result.Curve)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"{point.Date:yyyy-MM-dd},{point.Equity:R},{point.Weight:R},{point.Price:R}\n");
        }

        Write(path, text.ToString());
    }

    /// <summary>Writes one row per fill, including what each one cost.</summary>
    public static void WriteTrades(string path, BacktestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var text = new StringBuilder(
            "Date,Shares,FillPrice,ReferencePrice,Commission,Slippage,RealisedProfit,EquityAfter\n");
        foreach (TradeFill fill in result.Fills)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"{fill.Date:yyyy-MM-dd},{fill.Shares:R},{fill.Price:R},{fill.ReferencePrice:R},"
                + $"{fill.Commission:R},{fill.SlippageCost:R},{fill.RealisedProfit:R},{fill.EquityAfter:R}\n");
        }

        Write(path, text.ToString());
    }

    /// <summary>
    /// Writes the file, creating the directory it was asked for.
    /// </summary>
    /// <remarks>
    /// A missing directory is the common case for an output path typed on a command line, and
    /// failing the whole run after it has already printed a correct result would throw away the
    /// work over a folder.
    /// </remarks>
    private static void Write(string path, string content)
    {
        string? directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // No byte-order mark. Encoding.UTF8 emits one, and the first reader this file met -- a
        // three-line Python csv.DictReader -- saw the first column named with the mark still attached, and raised a
        // KeyError on "Date". This export exists so a result can be checked somewhere that is not
        // this program, and a header only this program can read defeats the entire purpose of it.
        File.WriteAllText(path, content, Core.Services.Utf8Files.WithoutBom);
    }
}
