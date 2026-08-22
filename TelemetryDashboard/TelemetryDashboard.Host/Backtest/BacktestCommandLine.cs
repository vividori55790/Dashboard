using System;
using System.IO;
using TelemetryDashboard.Core.Backtesting;
using TelemetryDashboard.Host.Configuration;

namespace TelemetryDashboard.Host.Backtest;

/// <summary>
/// The <c>backtest</c> subcommand: which file, which rule, and under what friction.
/// </summary>
/// <remarks>
/// A subcommand for the same reason <c>extensions</c> is one: it ends. A backtest reads a file,
/// prints a result and exits, and folding it into the server command line would mean every host
/// flag had to be accepted, and silently ignored, by a run that binds no socket.
/// </remarks>
public sealed class BacktestCommandLine
{
    /// <summary>The word that selects this subcommand.</summary>
    public const string Verb = "backtest";

    /// <summary>Directory of shipped sample price files, beside the executable.</summary>
    public const string SampleDirectoryName = "samples";

    /// <summary>Price file, resolved against the sample directory when it is a bare name.</summary>
    public string Path { get; private init; } = string.Empty;

    /// <summary>Strategy name, as <see cref="StrategyCatalogue"/> knows it.</summary>
    public string Strategy { get; private init; } = "sma-cross";

    /// <summary>Parameters for whichever strategy was named.</summary>
    public StrategyOptions Options { get; private init; } = new();

    /// <summary>Account and friction.</summary>
    public BacktestSettings Settings { get; private init; } = new();

    /// <summary>First session to include, or null for the start of the file.</summary>
    public DateOnly? From { get; private init; }

    /// <summary>Last session to include, or null for the end of the file.</summary>
    public DateOnly? To { get; private init; }

    /// <summary>Where to write the equity curve, or null to write none.</summary>
    public string? EquityOut { get; private init; }

    /// <summary>Where to write every fill, or null to write none.</summary>
    public string? TradesOut { get; private init; }

    /// <summary>Why the command line was rejected, or null.</summary>
    public string? Error { get; private init; }

    /// <summary>Whether <paramref name="args"/> selects this subcommand at all.</summary>
    public static bool Matches(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Verb, StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses <c>backtest &lt;file&gt; [options]</c>.</summary>
    public static BacktestCommandLine Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? path = null;
        string strategy = "sma-cross", equityOut = string.Empty, tradesOut = string.Empty;
        var options = new StrategyOptions();
        var settings = new BacktestSettings();
        DateOnly? from = null, to = null;

        for (int i = 1; i < args.Length; i++)
        {
            string flag = args[i];
            switch (flag)
            {
                case "--strategy":
                    if (!ArgumentCursor.TryValue(args, ref i, out strategy)) return Missing(flag);
                    break;
                case "--fast":
                    if (!BacktestValues.Count(args, ref i, out int fast)) return Missing(flag);
                    options = options with { Fast = fast };
                    break;
                case "--slow":
                    if (!BacktestValues.Count(args, ref i, out int slow)) return Missing(flag);
                    options = options with { Slow = slow };
                    break;
                case "--window":
                    if (!BacktestValues.Count(args, ref i, out int window)) return Missing(flag);
                    options = options with { Window = window };
                    break;
                case "--entry-z":
                    if (!BacktestValues.Number(args, ref i, out double entry)) return Missing(flag);
                    options = options with { EntryZ = entry };
                    break;
                case "--exit-z":
                    if (!BacktestValues.Number(args, ref i, out double exit)) return Missing(flag);
                    options = options with { ExitZ = exit };
                    break;
                case "--short":
                    options = options with { AllowShort = true };
                    break;
                case "--cash":
                    if (!BacktestValues.Number(args, ref i, out double cash)) return Missing(flag);
                    settings = settings with { StartingCash = cash };
                    break;
                case "--commission-bps":
                    if (!BacktestValues.Number(args, ref i, out double fee)) return Missing(flag);
                    settings = settings with { CommissionBps = fee };
                    break;
                case "--slippage-bps":
                    if (!BacktestValues.Number(args, ref i, out double slip)) return Missing(flag);
                    settings = settings with { SlippageBps = slip };
                    break;
                case "--price":
                    if (!ArgumentCursor.TryValue(args, ref i, out string field)) return Missing(flag);
                    if (field is "close") settings = settings with { Field = PriceField.Close };
                    else if (field is "adjclose" or "adjusted" or "adj-close") settings = settings with { Field = PriceField.AdjustedClose };
                    else return Refuse($"--price accepts 'close' or 'adjclose', not '{field}'.");
                    break;
                case "--from":
                    if (!BacktestValues.Date(args, ref i, out DateOnly first)) return Refuse($"{flag} needs a date as yyyy-MM-dd.");
                    from = first;
                    break;
                case "--to":
                    if (!BacktestValues.Date(args, ref i, out DateOnly last)) return Refuse($"{flag} needs a date as yyyy-MM-dd.");
                    to = last;
                    break;
                case "--equity-out":
                    if (!ArgumentCursor.TryValue(args, ref i, out equityOut)) return Missing(flag);
                    break;
                case "--trades-out":
                    if (!ArgumentCursor.TryValue(args, ref i, out tradesOut)) return Missing(flag);
                    break;
                default:
                    if (flag.StartsWith('-')) return Refuse($"unknown argument '{flag}'.");
                    if (path is not null) return Refuse($"'{flag}' is a second file; only one is accepted.");
                    path = flag;
                    break;
            }
        }

        if (path is null) return Refuse("a price file is required, e.g. 'backtest SPY' or 'backtest ./aapl.csv'.");
        if (from is not null && to is not null && from > to) return Refuse("--from is after --to.");
        if (settings.Validate() is { } problem) return Refuse(problem);

        return new BacktestCommandLine
        {
            Path = Resolve(path),
            Strategy = strategy,
            Options = options,
            Settings = settings,
            From = from,
            To = to,
            EquityOut = equityOut.Length == 0 ? null : equityOut,
            TradesOut = tradesOut.Length == 0 ? null : tradesOut
        };
    }

    /// <summary>
    /// Takes a bare symbol to mean the shipped sample of that name.
    /// </summary>
    /// <remarks>
    /// Only when the argument names nothing on disk, so a real file always wins over a sample that
    /// happens to share its name. Without this the shortest command that demonstrates the feature
    /// has to spell out a path into the install directory, and an example nobody can run in one
    /// line is an example nobody runs.
    /// </remarks>
    private static string Resolve(string argument)
    {
        if (File.Exists(argument)) return argument;

        string sample = System.IO.Path.Combine(
            AppContext.BaseDirectory, SampleDirectoryName, argument + ".csv");
        return File.Exists(sample) ? sample : argument;
    }

    private static BacktestCommandLine Missing(string flag) => Refuse($"{flag} needs a value.");

    private static BacktestCommandLine Refuse(string message) => new() { Error = message };
}
