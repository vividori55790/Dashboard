namespace TelemetryDashboard.Core.Backtesting.Strategies;

/// <summary>
/// Buys once on the first bar and holds to the end.
/// </summary>
/// <remarks>
/// The benchmark, and the reason every run reports one. A strategy that returned 90 % over a decade
/// has said nothing until you know the symbol returned 240 % for doing nothing — and most rules
/// that look clever on a chart lose to this one after costs, which is the single most useful fact a
/// backtester can tell someone.
/// <para>
/// It is run through the same engine as the strategy under test, so it pays the same commission and
/// the same slippage on its one entry, and it is filled the same way. A benchmark computed as a
/// simple ratio of last price to first would be a free-of-charge comparison against a strategy that
/// pays, which flatters the strategy by exactly the amount trading costs.
/// </para>
/// </remarks>
public sealed class BuyAndHoldStrategy : IBarStrategy
{
    /// <inheritdoc />
    public string Name => "buy-and-hold";

    /// <inheritdoc />
    public int WarmUpBars => 0;

    /// <inheritdoc />
    public void Reset()
    {
        // Nothing is remembered between bars: the answer is the same on every one of them.
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns the same full weight every bar rather than only on the first. The engine skips a
    /// trade that would not move the position, so this produces one entry and then nothing —
    /// while also being the correct answer if equity drifts far enough that holding would need a
    /// top-up, instead of a rule that quietly stops applying after bar zero.
    /// </remarks>
    public double? Decide(StrategyContext context) => 1.0;
}
