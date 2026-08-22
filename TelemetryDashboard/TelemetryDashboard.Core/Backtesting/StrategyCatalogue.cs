using System;
using System.Collections.Generic;
using TelemetryDashboard.Core.Backtesting.Strategies;

namespace TelemetryDashboard.Core.Backtesting;

/// <summary>Parameters every catalogued strategy draws the ones it needs from.</summary>
/// <remarks>
/// One flat record rather than a shape per strategy, so a command line can be parsed before it is
/// known which rule the flags belong to. The cost is that a flag can be supplied to a strategy that
/// ignores it, which <see cref="StrategyCatalogue"/> reports rather than accepting in silence — a
/// run configured with <c>--fast 5</c> against a rule that has no fast average is not the run the
/// person asked for.
/// </remarks>
public sealed record StrategyOptions
{
    /// <summary>Short average, in bars.</summary>
    public int Fast { get; init; } = 50;

    /// <summary>Long average, in bars.</summary>
    public int Slow { get; init; } = 200;

    /// <summary>Lookback for the mean-reversion rule, in bars.</summary>
    public int Window { get; init; } = 20;

    /// <summary>Standard deviations from the mean at which a position is opened.</summary>
    public double EntryZ { get; init; } = 2.0;

    /// <summary>Standard deviations at which it is closed again.</summary>
    public double ExitZ { get; init; } = 0.5;

    /// <summary>Whether a bearish signal shorts rather than standing aside.</summary>
    public bool AllowShort { get; init; }
}

/// <summary>The strategies a run can be asked for by name.</summary>
public static class StrategyCatalogue
{
    /// <summary>Name of the rule every run is measured against.</summary>
    public const string Benchmark = "buy-and-hold";

    /// <summary>Names accepted by <see cref="TryCreate"/>, with one line each.</summary>
    public static IReadOnlyDictionary<string, string> Descriptions { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sma-cross"] = "Hold while a fast average is above a slow one. --fast, --slow, --short.",
            ["mean-reversion"] = "Buy what has fallen far below its own mean. --window, --entry-z, --exit-z, --short.",
            [Benchmark] = "Buy on the first session and hold. The benchmark; takes no parameters."
        };

    /// <summary>Builds the named strategy, or explains why it cannot be built.</summary>
    public static bool TryCreate(
        string name, StrategyOptions options, out IBarStrategy? strategy, out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);
        strategy = null;
        error = null;

        try
        {
            switch (name?.ToLowerInvariant())
            {
                case "sma-cross" or "sma" or "ma-cross":
                    strategy = new MovingAverageCrossStrategy(options.Fast, options.Slow, options.AllowShort);
                    return true;

                case "mean-reversion" or "mean" or "reversion":
                    strategy = new MeanReversionStrategy(
                        options.Window, options.EntryZ, options.ExitZ, options.AllowShort);
                    return true;

                case Benchmark or "hold" or "bh":
                    strategy = new BuyAndHoldStrategy();
                    return true;

                default:
                    error = $"unknown strategy '{name}'. Known: {string.Join(", ", Descriptions.Keys)}.";
                    return false;
            }
        }
        catch (ArgumentOutOfRangeException ex)
        {
            // The strategies refuse parameter combinations that cannot produce a meaningful run --
            // two equal averages that never cross, an entry threshold inside the exit threshold.
            // Surfaced as a message rather than a stack trace: it is a typo on a command line. The
            // framework appends " (Parameter 'x')", which names an argument the person never typed.
            int appended = ex.Message.IndexOf(" (Parameter", StringComparison.Ordinal);
            error = (appended < 0 ? ex.Message : ex.Message[..appended]).Trim();
            return false;
        }
    }
}
