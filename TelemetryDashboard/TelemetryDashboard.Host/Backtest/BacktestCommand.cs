using System;
using System.IO;
using System.Linq;
using TelemetryDashboard.Core.Backtesting;
using TelemetryDashboard.Core.Backtesting.Strategies;

namespace TelemetryDashboard.Host.Backtest;

/// <summary>
/// Executes <c>backtest &lt;file&gt; [options]</c> and ends the process.
/// </summary>
/// <remarks>
/// The rule and the benchmark are run through <em>one</em> engine, one after the other. Two engines
/// would also work and would hide a real risk: an engine whose per-run reset is incomplete carries
/// cash or a position from the first run into the second, and the failure surfaces as a benchmark
/// column that is quietly wrong — the one column a reader uses to sanity-check everything else.
/// Reusing the instance means the reset is exercised every single time this command is run.
/// </remarks>
public static class BacktestCommand
{
    /// <summary>Exit code for a file that exists and holds nothing a run can be built from.</summary>
    public const int ExitNoData = 74;

    /// <summary>Runs the subcommand named in <paramref name="args"/>, whose first word is 'backtest'.</summary>
    public static int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 1 || args.Contains("--help") || args.Contains("-h"))
        {
            Console.Out.Write(BacktestUsageText.Render());
            return 0;
        }

        BacktestCommandLine command = BacktestCommandLine.Parse(args);
        if (command.Error is not null) return Refuse(command.Error, withUsage: true);

        PriceCsvLoad load = PriceCsvReader.ReadFile(command.Path);
        if (load.Series is null)
        {
            string hint = File.Exists(command.Path)
                ? string.Empty
                : $"{Environment.NewLine}  Samples ship in {SampleDirectory()} -- "
                  + $"'{BacktestCommandLine.Verb} --help' lists what a price file should look like.";
            return Refuse($"{load.Error}{hint}", withUsage: false, ExitNoData);
        }

        BarSeries? window = load.Series.Slice(command.From, command.To);
        if (window is null)
        {
            return Refuse(
                $"the requested window holds no session. {load.Series.Symbol} covers "
                + $"{load.Series.FirstDate:yyyy-MM-dd} to {load.Series.LastDate:yyyy-MM-dd}.",
                withUsage: false, ExitNoData);
        }

        if (!StrategyCatalogue.TryCreate(command.Strategy, command.Options, out IBarStrategy? strategy, out string? why))
        {
            return Refuse(why!, withUsage: true);
        }

        if (window.Count <= strategy!.WarmUpBars)
        {
            // Every bar would be spent warming up, so the run can only report the starting balance
            // and a flat line. Better to say why than to print a result that looks like a verdict
            // on the strategy when it is a verdict on the window.
            return Refuse(
                $"{strategy.Name} needs {strategy.WarmUpBars} session(s) of warm-up and the window "
                + $"holds {window.Count}. Widen --from/--to, or shorten the strategy's periods.",
                withUsage: false, ExitNoData);
        }

        var engine = new BacktestEngine(command.Settings);
        BacktestResult run = engine.Run(window, strategy);
        BacktestResult benchmark = engine.Run(window, new BuyAndHoldStrategy());

        Console.Out.Write(BacktestReport.Render(run, benchmark, load.Notes()));
        return Export(command, run);
    }

    /// <summary>Writes whichever exports were asked for, reporting where each landed.</summary>
    private static int Export(BacktestCommandLine command, BacktestResult run)
    {
        try
        {
            if (command.EquityOut is not null)
            {
                BacktestCsvExport.WriteEquity(command.EquityOut, run);
                Console.WriteLine($"Equity curve written to {Path.GetFullPath(command.EquityOut)} "
                    + $"({run.Curve.Count} rows).");
            }

            if (command.TradesOut is not null)
            {
                BacktestCsvExport.WriteTrades(command.TradesOut, run);
                Console.WriteLine($"Fills written to {Path.GetFullPath(command.TradesOut)} "
                    + $"({run.Fills.Count} rows).");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // The result is already printed and correct. The export failing is worth a non-zero
            // exit so a script notices, but not worth pretending the run did not happen.
            Console.Error.WriteLine($"telemetry-host {BacktestCommandLine.Verb}: "
                + $"the run finished but its export could not be written: {ex.Message}");
            return ExitNoData;
        }

        return 0;
    }

    private static string SampleDirectory() =>
        Path.Combine(AppContext.BaseDirectory, BacktestCommandLine.SampleDirectoryName);

    private static int Refuse(string message, bool withUsage, int code = Program.ExitUsage)
    {
        Console.Error.WriteLine($"telemetry-host {BacktestCommandLine.Verb}: {message}");
        if (withUsage) Console.Error.WriteLine(BacktestUsageText.Render());
        return code;
    }
}
