namespace TelemetryDashboard.Core.Backtesting;

/// <summary>
/// Decides how much of the account should be in the symbol, once per closed bar.
/// </summary>
/// <remarks>
/// A strategy returns a target <em>weight</em>, not an order. Weights compose: the same answer means
/// the same exposure whether the account holds ten thousand or a million, so a strategy cannot
/// accidentally encode the account size it was written against, and a run at a different starting
/// balance is the same experiment rather than a different one.
/// <para>
/// Implementations may hold state across bars — a moving average is state — so the engine promises
/// exactly two things: <see cref="Reset"/> is called before the first bar of a run, and
/// <see cref="Decide"/> is then called once per bar in ascending date order with none skipped. A
/// strategy that is fed out of order gets a rolling window mixing sessions from different years,
/// and nothing about the resulting equity curve looks wrong.
/// </para>
/// </remarks>
public interface IBarStrategy
{
    /// <summary>Name as it appears in the report.</summary>
    string Name { get; }

    /// <summary>
    /// Bars needed before this strategy's answer means anything.
    /// </summary>
    /// <remarks>
    /// Declared rather than inferred so the report can say how much of the series was spent warming
    /// up. A 200-day average over a one-year file spends four months of it deciding nothing, and a
    /// backtest that presents the remainder as "one year" is overstating its own evidence.
    /// </remarks>
    int WarmUpBars { get; }

    /// <summary>Forgets everything from a previous run.</summary>
    void Reset();

    /// <summary>
    /// The fraction of equity to hold after this bar: 1 fully long, 0 flat, -1 fully short.
    /// </summary>
    /// <remarks>
    /// Returning null means "leave the position where it is", which is different from returning the
    /// current weight: null cannot generate a trade at all, so a strategy still warming up cannot
    /// churn the account with rounding-sized corrections while it waits.
    /// </remarks>
    double? Decide(StrategyContext context);
}
