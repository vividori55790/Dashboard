using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TelemetryDashboard.Core.Backtesting;

/// <summary>
/// Renders a run beside its benchmark, followed by everything that qualifies it.
/// </summary>
/// <remarks>
/// Two columns, never one. A single column of impressive numbers is how a backtest persuades
/// somebody of something untrue, and the benchmark is the cheapest available correction: it is
/// computed by the same engine, over the same sessions, paying the same commission and the same
/// slippage, so the only thing that differs between the columns is the rule.
/// <para>
/// The qualifications are printed with the result rather than left in documentation. Warm-up spent,
/// rows the file lost, a position still open at the end, a Sharpe against a zero risk-free rate —
/// each of those changes what the figures above mean, and a reader who has to go looking for them
/// will not.
/// </para>
/// </remarks>
public static class BacktestReport
{
    /// <summary>Renders <paramref name="run"/> against <paramref name="benchmark"/>.</summary>
    public static string Render(BacktestResult run, BacktestResult benchmark, IEnumerable<string>? dataNotes = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(benchmark);

        PerformanceMetrics m = run.Metrics;
        var text = new StringBuilder();

        text.AppendLine($"Backtest  {run.Symbol}  {run.FirstDate:yyyy-MM-dd} .. {run.LastDate:yyyy-MM-dd}"
            + $"  ({run.Curve.Count} sessions, {m.BarsPerYear:0.#}/yr "
            + $"{(m.BarsPerYearMeasured ? "measured" : "assumed -- span too short to measure")})");
        text.AppendLine($"Strategy  {run.StrategyName}");
        text.AppendLine($"Account   {Money(run.Settings.StartingCash)} start, "
            + $"commission {run.Settings.CommissionBps:0.##} bp, slippage {run.Settings.SlippageBps:0.##} bp, "
            + $"marked on {(run.Settings.Field == PriceField.AdjustedClose ? "adjusted close" : "close")}");
        text.AppendLine();

        text.AppendLine($"  {"",-18}{"strategy",14}{StrategyCatalogue.Benchmark,18}");
        Row(text, "final equity", Money(run.FinalEquity), Money(benchmark.FinalEquity));
        Row(text, "total return", Signed(m.TotalReturn), Signed(benchmark.Metrics.TotalReturn));
        Row(text, "CAGR", Signed(m.Cagr), Signed(benchmark.Metrics.Cagr));
        Row(text, "max drawdown", Signed(-m.MaxDrawdown), Signed(-benchmark.Metrics.MaxDrawdown));
        Row(text, "volatility", Magnitude(m.Volatility), Magnitude(benchmark.Metrics.Volatility));
        Row(text, "Sharpe", Ratio(m.Sharpe), Ratio(benchmark.Metrics.Sharpe));
        Row(text, "Sortino", Ratio(m.Sortino), Ratio(benchmark.Metrics.Sortino));
        text.AppendLine();
        Row(text, "round trips", run.RoundTrips.Count.ToString(CultureInfo.InvariantCulture),
            benchmark.RoundTrips.Count.ToString(CultureInfo.InvariantCulture));
        Row(text, "win rate", Magnitude(run.WinRate), Magnitude(benchmark.WinRate));
        Row(text, "time invested", Magnitude(run.Exposure), Magnitude(benchmark.Exposure));
        Row(text, "costs paid", Money(run.CommissionPaid + run.SlippagePaid),
            Money(benchmark.CommissionPaid + benchmark.SlippagePaid));
        text.AppendLine();

        text.AppendLine($"  Deepest fall was {Signed(-m.MaxDrawdown)} into {m.MaxDrawdownDate:yyyy-MM-dd}; "
            + $"the benchmark's was {Signed(-benchmark.Metrics.MaxDrawdown)} into "
            + $"{benchmark.Metrics.MaxDrawdownDate:yyyy-MM-dd}.");
        text.AppendLine($"  Costs took {Magnitude(run.CostDrag)} of the opening balance across "
            + $"{run.Fills.Count} fill(s).");
        text.AppendLine();

        text.AppendLine("What these numbers do not say");
        foreach (string caveat in Caveats(run, dataNotes)) text.AppendLine($"  - {caveat}");
        text.AppendLine();

        text.AppendLine(run.UnexecutedFinalSignal is { } signal
            ? $"As of {run.LastDate:yyyy-MM-dd} the rule says hold {Signed(signal)} of the account. "
              + "No session followed, so this was not filled."
            : $"As of {run.LastDate:yyyy-MM-dd} the rule asked for no change.");

        return text.ToString();
    }

    private static IEnumerable<string> Caveats(BacktestResult run, IEnumerable<string>? dataNotes)
    {
        if (dataNotes is not null)
        {
            foreach (string note in dataNotes) yield return note;
        }

        if (run.WarmUpBars > 0)
        {
            DateOnly armed = run.Curve[Math.Min(run.WarmUpBars, run.Curve.Count - 1)].Date;
            yield return $"{run.WarmUpBars} session(s) of warm-up: no position was possible before "
                + $"{armed:yyyy-MM-dd}, though the returns above are quoted over the whole span.";
        }

        if (run.EndedWithOpenPosition)
        {
            yield return "The run ended still holding. That position is marked at the last close and "
                + "was never sold, so the cost of getting out of it is in none of these figures.";
        }

        if (run.RoundTrips.Count == 0)
        {
            yield return "No position was opened and closed, so the win rate is unknown rather than zero.";
        }

        if (run.Metrics.Ruined)
        {
            yield return "Equity reached zero during the run. Anything annualised past that point is arithmetic.";
        }

        yield return "Sharpe and Sortino assume a risk-free rate of zero, which flatters both by "
            + "roughly the cash rate of the period.";
        yield return "Borrow costs, margin, taxes and market impact are not modelled; slippage is a "
            + "flat rate that does not grow with order size.";
        yield return "This is one rule on one symbol over one period. It is a measurement, not a forecast.";
    }

    private static void Row(StringBuilder text, string label, string left, string right) =>
        text.AppendLine($"  {label,-18}{left,14}{right,18}");

    private static string Money(double value) =>
        double.IsFinite(value) ? value.ToString("N2", CultureInfo.InvariantCulture) : "n/a";

    /// <summary>A quantity whose direction matters, so it always carries its sign.</summary>
    private static string Signed(double fraction) =>
        double.IsFinite(fraction)
            ? (fraction * 100).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " %"
            : "n/a";

    /// <summary>
    /// A quantity that has no direction -- a volatility, a win rate, a share of time.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Signed"/> because forcing a sign onto these was actively
    /// misleading: the first run of this printed a volatility of "+15.1 %" next to a return of
    /// "+138.9 %", in the same column, in the same units, and the plus sign is the only thing a
    /// reader has to tell a gain from a spread.
    /// </remarks>
    private static string Magnitude(double fraction) =>
        double.IsFinite(fraction)
            ? (fraction * 100).ToString("0.0", CultureInfo.InvariantCulture) + " %"
            : "n/a";

    private static string Ratio(double value) =>
        double.IsFinite(value) ? value.ToString("0.00", CultureInfo.InvariantCulture) : "n/a";
}
