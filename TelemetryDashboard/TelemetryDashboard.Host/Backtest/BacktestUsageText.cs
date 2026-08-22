using System;
using System.Linq;
using TelemetryDashboard.Core.Backtesting;

namespace TelemetryDashboard.Host.Backtest;

/// <summary>The <c>backtest</c> help screen.</summary>
/// <remarks>
/// Beside the parser, for the reason <see cref="Configuration.UsageText"/> is beside its own: an
/// option cannot be added without the line documenting it sitting one file away.
/// </remarks>
public static class BacktestUsageText
{
    /// <summary>Renders the help screen, listing the strategies from the catalogue itself.</summary>
    public static string Render()
    {
        string strategies = string.Join(Environment.NewLine, StrategyCatalogue.Descriptions
            .Select(entry => $"                          {entry.Key,-16}{entry.Value}"));

        return $"""
        Replays a daily price file through a trading rule and reports what it would have done.

        Usage:
          TelemetryDashboard.Host {BacktestCommandLine.Verb} <file.csv|symbol> [options]

        The file is a vendor daily export -- Date,Open,High,Low,Close,Adj Close,Volume, as Yahoo
        Finance writes it, or the same without the adjusted column, as Stooq does. A bare name with
        no such file resolves to the shipped sample of that name in ./{BacktestCommandLine.SampleDirectoryName}/.

        A decision made from one session's close is filled at the NEXT session's open. That is the
        difference between a simulation and a machine that can see tomorrow, and it is why the last
        session's signal is reported rather than traded.

        Options:
          --strategy <name>     Which rule to run. Default sma-cross.
        {strategies}
          --fast <bars>         Short average for sma-cross. Default 50.
          --slow <bars>         Long average for sma-cross. Default 200.
          --window <bars>       Lookback for mean-reversion. Default 20.
          --entry-z <sigma>     Standard deviations from the mean at which mean-reversion buys.
                                Default 2. Must exceed --exit-z, or the rule opens and closes on
                                the same bar and pays commission for the privilege.
          --exit-z <sigma>      Standard deviations at which it lets go again. Default 0.5.
          --short               Take the bearish side rather than standing aside. Off by default:
                                a short's losses are unbounded, so it should be asked for.
          --cash <amount>       Starting balance. Default 10000.
          --commission-bps <n>  Commission on the value traded, in basis points. Default 5.
          --slippage-bps <n>    How far a fill moves against the order, in basis points. Default 2.
                                Both default to non-zero on purpose. A frictionless backtest
                                flatters exactly those rules that trade most, which are the ones
                                least likely to survive contact with a broker.
          --price <field>       'adjclose' (default) or 'close'. The adjusted close folds splits and
                                dividends back in; the raw close shows a split as a crash that never
                                happened.
          --from <yyyy-MM-dd>   First session to include. Default: the start of the file.
          --to <yyyy-MM-dd>     Last session to include. Default: the end of the file.
          --equity-out <file>   Write the equity curve as CSV, so the result can be recomputed
                                somewhere that is not this program.
          --trades-out <file>   Write every fill as CSV, with what each one cost.

        Every run is reported beside buy-and-hold over the same sessions, through the same engine,
        paying the same commission and the same slippage. A return quoted without that comparison
        does not say whether the rule did anything.

        Examples:
          TelemetryDashboard.Host {BacktestCommandLine.Verb} SPY
          TelemetryDashboard.Host {BacktestCommandLine.Verb} AAPL --strategy mean-reversion --window 20
          TelemetryDashboard.Host {BacktestCommandLine.Verb} ./my-export.csv --from 2020-01-01 --commission-bps 0

        """;
    }
}
