using TelemetryDashboard.Core.Backtesting;
using TelemetryDashboard.Core.Backtesting.Strategies;

namespace TelemetryDashboard.Tests;

/// <summary>
/// What the simulator is allowed to know, and what it must be made to pay.
/// </summary>
/// <remarks>
/// A backtester fails in one direction. Every mistake in it — filling at a price the decision was
/// made from, charging no commission, letting a strategy read a later bar — makes the result better
/// than the truth, never worse, and none of them produces an output that looks wrong. So these
/// tests are mostly about the engine refusing advantages rather than about it computing correctly.
/// </remarks>
public class BacktestEngineTests
{
    /// <summary>A rule that asks for one fixed weight on every bar.</summary>
    private sealed class FixedWeight(double weight) : IBarStrategy
    {
        public string Name => $"fixed({weight})";
        public int WarmUpBars => 0;
        public void Reset() { }
        public double? Decide(StrategyContext context) => weight;
    }

    /// <summary>A rule that records what it was allowed to see.</summary>
    private sealed class Recording : IBarStrategy
    {
        public List<int> Indices { get; } = new();
        public List<int> Counts { get; } = new();
        public int Resets { get; private set; }

        public string Name => "recording";
        public int WarmUpBars => 0;
        public void Reset() => Resets++;

        public double? Decide(StrategyContext context)
        {
            Indices.Add(context.Index);
            Counts.Add(context.Count);
            return null;
        }
    }

    private static BarSeries Series(params (string Date, double Open, double Close)[] bars) =>
        BarSeries.Create("TEST", bars.Select(b => new PriceBar
        {
            Date = DateOnly.Parse(b.Date),
            Open = b.Open,
            High = Math.Max(b.Open, b.Close),
            Low = Math.Min(b.Open, b.Close),
            Close = b.Close,
            AdjustedClose = b.Close
        }), out _);

    private static BacktestSettings Frictionless => new()
    {
        StartingCash = 10_000,
        CommissionBps = 0,
        SlippageBps = 0,
        Field = PriceField.Close
    };

    // ---- the one that matters ------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void ADecisionMadeFromOneCloseIsFilledAtTheNextSessionsOpen()
    {
        // The single defect that separates a simulation from a machine that can see the future.
        // The opens are deliberately far from the closes so a fill at the wrong one is unmissable.
        BarSeries series = Series(
            ("2024-01-02", 100, 110),
            ("2024-01-03", 200, 210),
            ("2024-01-04", 300, 310));

        BacktestResult result = new BacktestEngine(Frictionless).Run(series, new FixedWeight(1.0));

        result.Fills.Should().ContainSingle();
        result.Fills[0].Date.Should().Be(new DateOnly(2024, 1, 3), "the decision was made when the 2nd closed");
        result.Fills[0].ReferencePrice.Should().Be(200,
            "200 is the open of the session after the one the decision was read from; 110 would be "
            + "buying at a price the market had already stopped offering");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AStrategyIsOnlyEverShownItsOwnBarAndTheOnesBeforeIt()
    {
        BarSeries series = Series(
            ("2024-01-02", 100, 110), ("2024-01-03", 111, 120), ("2024-01-04", 121, 130));
        var strategy = new Recording();

        new BacktestEngine(Frictionless).Run(series, strategy);

        // Every bar offered, in ascending order, none skipped -- and the history handed over never
        // extends past the bar being decided on.
        strategy.Indices.Should().Equal(new[] { 0, 1, 2 });
        strategy.Counts.Should().Equal(new[] { 1, 2, 3 });
        strategy.Resets.Should().Be(1, "the engine promises exactly one reset per run");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ReachingForwardFromAContextIsRefusedRatherThanClamped()
    {
        // Structural rather than incidental: a strategy that peeks at tomorrow should not be able
        // to get a plausible number back, because a plausible number is what makes look-ahead
        // survive review.
        BarSeries series = Series(("2024-01-02", 100, 110), ("2024-01-03", 111, 120));
        var context = new StrategyContext(series, 0, PriceField.Close, 0);

        context.Invoking(c => c.Ago(-1)).Should().Throw<ArgumentOutOfRangeException>();
        context.Invoking(c => c.Ago(1)).Should().Throw<ArgumentOutOfRangeException>();
        context.Ago(0).Close.Should().Be(110);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheLastSessionsDecisionIsReportedRatherThanFilled()
    {
        BarSeries series = Series(("2024-01-02", 100, 110), ("2024-01-03", 200, 210));

        BacktestResult result = new BacktestEngine(Frictionless).Run(series, new FixedWeight(1.0));

        result.UnexecutedFinalSignal.Should().Be(1.0);
        result.Fills.Should().ContainSingle("there is no session after the last one to fill in");
    }

    // ---- friction ------------------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void SlippageMovesTheFillAgainstTheOrderInBothDirections()
    {
        BarSeries series = Series(
            ("2024-01-02", 100, 100), ("2024-01-03", 100, 100), ("2024-01-04", 100, 100));
        var settings = new BacktestSettings
        {
            StartingCash = 10_000, CommissionBps = 0, SlippageBps = 100, Field = PriceField.Close
        };

        // Long on the first decision, flat on the second, so one buy and one sell are both filled.
        var alternating = new Queue<double?>(new double?[] { 1.0, 0.0, 0.0 });
        BacktestResult result = new BacktestEngine(settings).Run(series, new Scripted(alternating));

        result.Fills.Should().HaveCount(2);
        result.Fills[0].Price.Should().Be(101, "a buy pays a hundred basis points more");
        result.Fills[1].Price.Should().Be(99, "a sell receives a hundred basis points less");
        result.Fills.Should().OnlyContain(f => f.SlippageCost > 0, "slippage can never help");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void CommissionIsChargedOnWhatWasTradedAndIsCarriedOutOfTheRun()
    {
        BarSeries series = Series(("2024-01-02", 100, 100), ("2024-01-03", 100, 100));
        var settings = new BacktestSettings
        {
            StartingCash = 10_000, CommissionBps = 10, SlippageBps = 0, Field = PriceField.Close
        };

        BacktestResult result = new BacktestEngine(settings).Run(series, new FixedWeight(1.0));

        result.CommissionPaid.Should().BeApproximately(10.0, 1e-9, "10 bp of a 10,000 notional");
        result.FinalEquity.Should().BeApproximately(9_990.0, 1e-9,
            "the commission leaves the account; a backtest that reports it and does not deduct it "
            + "is reporting a cost nobody paid");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void HoldingAConstantWeightDoesNotTradeOnEveryBar()
    {
        // Without a floor on trade size, equity moving with the price puts the position a few
        // ten-thousandths off target every morning, and the correction pays commission on the whole
        // account hundreds of times over a decade -- invisibly, one rounding error at a time.
        BarSeries series = Series(Enumerable.Range(0, 200)
            .Select(i => (
                Date: new DateOnly(2024, 1, 1).AddDays(i).ToString("yyyy-MM-dd"),
                Open: 100.0 + i * 0.01,
                Close: 100.0 + i * 0.01))
            .ToArray());

        BacktestResult result = new BacktestEngine(Frictionless).Run(series, new FixedWeight(1.0));

        result.Fills.Should().ContainSingle("one entry, then nothing worth placing");
    }

    // ---- reuse ---------------------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void ASecondRunOnTheSameEngineLeavesTheFirstResultAlone()
    {
        // Regression. The engine copied its equity curve and its fills out and handed over the
        // round-trip tracker's live list, so running the benchmark through the same engine cleared
        // the strategy's trips: a report printed nine fills beside zero round trips and a win rate
        // of "n/a" for a rule that had opened and closed four times. Found by reading the output of
        // the real binary, not by a test, because every test until now held one result at a time.
        BarSeries series = Series(
            ("2024-01-02", 100, 100), ("2024-01-03", 100, 100),
            ("2024-01-04", 100, 100), ("2024-01-05", 100, 100));
        var engine = new BacktestEngine(Frictionless);

        BacktestResult first = engine.Run(series, new Scripted(new Queue<double?>(new double?[] { 1.0, 0.0, 0.0, 0.0 })));
        int trips = first.RoundTrips.Count;
        int fills = first.Fills.Count;
        trips.Should().Be(1, "the position was opened and closed once");

        engine.Run(series, new BuyAndHoldStrategy());

        first.RoundTrips.Should().HaveCount(trips, "the earlier result is a record, not a view");
        first.Fills.Should().HaveCount(fills);
    }

    /// <summary>A rule that reads its answers off a script, so a test can place exact trades.</summary>
    private sealed class Scripted(Queue<double?> answers) : IBarStrategy
    {
        public string Name => "scripted";
        public int WarmUpBars => 0;
        public void Reset() { }
        public double? Decide(StrategyContext context) => answers.Count > 0 ? answers.Dequeue() : null;
    }
}
