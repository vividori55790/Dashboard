using TelemetryDashboard.Core.Backtesting;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Reading an equity curve, and counting a position that crosses through flat.
/// </summary>
public class BacktestMetricsTests
{
    private static IReadOnlyList<EquityPoint> Curve(DateOnly start, params double[] equity) =>
        equity.Select((e, i) => new EquityPoint
        {
            Date = start.AddDays(i),
            Equity = e,
            Weight = 1,
            Price = e
        }).ToArray();

    // ---- the path, not the endpoints -----------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheDrawdownIsMeasuredFromThePeakThatCameBeforeIt()
    {
        // 100 up to 200, down to 120, back to 260. The fall is 40 % of 200, not 40 % of 260 and
        // not the 20 % it would be if measured against where it started.
        PerformanceMetrics m = PerformanceMetrics.From(
            Curve(new DateOnly(2024, 1, 1), 100, 200, 120, 260));

        m.MaxDrawdown.Should().BeApproximately(0.40, 1e-12);
        m.MaxDrawdownDate.Should().Be(new DateOnly(2024, 1, 3));
        m.TotalReturn.Should().BeApproximately(1.60, 1e-12,
            "the two accounts that end here are not the same investment, which is why both are reported");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void CompoundingTheReportedCagrReproducesTheReportedTotalReturn()
    {
        // The check that catches an annualisation using the wrong span: they must agree.
        var start = new DateOnly(2020, 1, 1);
        double[] equity = Enumerable.Range(0, 1461).Select(i => 1000.0 * Math.Pow(1.0002, i)).ToArray();

        PerformanceMetrics m = PerformanceMetrics.From(Curve(start, equity));
        double years = TradingCalendar.YearsBetween(start, start.AddDays(equity.Length - 1));

        Math.Pow(1 + m.Cagr, years).Should().BeApproximately(1 + m.TotalReturn, 1e-9);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AnAccountThatNeverMovedHasNoSharpeRatherThanAZeroOne()
    {
        // Zero would read as "measured, and mediocre". There is no ratio here: the denominator is
        // zero because nothing happened, and saying so is the only honest answer.
        PerformanceMetrics m = PerformanceMetrics.From(
            Curve(new DateOnly(2024, 1, 1), 100, 100, 100, 100));

        m.Sharpe.Should().Be(double.NaN);
        m.Volatility.Should().Be(0);
        m.MaxDrawdown.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AnAccountThatReachedZeroSaysSoInsteadOfAnnualisingPastIt()
    {
        PerformanceMetrics m = PerformanceMetrics.From(
            Curve(new DateOnly(2024, 1, 1), 100, 50, 0, 0));

        m.Ruined.Should().BeTrue();
        m.Cagr.Should().Be(double.NaN, "no constant rate compounds to nothing");
        m.MaxDrawdown.Should().Be(1.0);
    }

    // ---- annualisation --------------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void BarsPerYearIsCountedFromTheDatesRatherThanAssumedToBe252()
    {
        // Assuming the daily constant for a weekly file overstates annualised volatility by more
        // than a factor of two, and nothing in the output would look unusual.
        var start = new DateOnly(2020, 1, 6);
        double[] weekly = Enumerable.Range(0, 261).Select(i => 100.0 + i).ToArray();
        var curve = weekly.Select((e, i) => new EquityPoint
        {
            Date = start.AddDays(i * 7), Equity = e, Weight = 1, Price = e
        }).ToArray();

        PerformanceMetrics m = PerformanceMetrics.From(curve);

        m.BarsPerYear.Should().BeApproximately(52.2, 0.2);
        m.BarsPerYearMeasured.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ASpanTooShortToMeasureFallsBackToTheConventionAndAdmitsIt()
    {
        var curve = new[]
        {
            new EquityPoint { Date = new DateOnly(2024, 5, 1), Equity = 100, Weight = 0, Price = 100 }
        };

        PerformanceMetrics m = PerformanceMetrics.From(curve);

        m.BarsPerYear.Should().Be(252);
        m.BarsPerYearMeasured.Should().BeFalse("the report says 'assumed' so nobody reads it as measured");
    }

    // ---- round trips ----------------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void ATradeThatCarriesThePositionThroughFlatEndsOneTripAndBeginsAnother()
    {
        // The case that is easy to write wrongly and impossible to notice: the profit realised
        // belongs only to the part that closed, and the cost belongs partly to each side.
        var tracker = new RoundTripTracker();
        var day = new DateOnly(2024, 1, 1);

        tracker.Record(day, before: 0, after: 10, realised: 0, cost: 2);
        tracker.Record(day.AddDays(5), before: 10, after: -10, realised: 100, cost: 8);

        tracker.Closed.Should().ContainSingle();
        tracker.Closed[0].GrossProfit.Should().Be(100);
        tracker.Closed[0].Costs.Should().Be(2 + 4, "half the flip's cost closed the long, half opened the short");
        tracker.Closed[0].Direction.Should().Be(1);
        tracker.HasOpenTrip.Should().BeTrue("the short side of the flip is still open");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ATripIsWonOrLostOnWhatItLeftBehindAfterCosts()
    {
        // A rule whose winners are thinner than its commission is a losing rule, and counting it as
        // a winner is precisely the flattering error a win rate invites.
        var tracker = new RoundTripTracker();
        var day = new DateOnly(2024, 1, 1);

        tracker.Record(day, before: 0, after: 10, realised: 0, cost: 6);
        tracker.Record(day.AddDays(1), before: 10, after: 0, realised: 5, cost: 0);

        tracker.Closed[0].GrossProfit.Should().Be(5);
        tracker.Closed[0].NetProfit.Should().Be(-1);
        tracker.Closed[0].IsWin.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ScalingIntoAPositionOverSeveralBuysIsStillOneTrip()
    {
        // Counted per fill instead, the same behaviour reports a 25 % or a 75 % win rate depending
        // only on how the orders happened to be sliced.
        var tracker = new RoundTripTracker();
        var day = new DateOnly(2024, 1, 1);

        tracker.Record(day, before: 0, after: 5, realised: 0, cost: 1);
        tracker.Record(day.AddDays(1), before: 5, after: 10, realised: 0, cost: 1);
        tracker.Record(day.AddDays(2), before: 10, after: 15, realised: 0, cost: 1);
        tracker.Record(day.AddDays(3), before: 15, after: 0, realised: 60, cost: 1);

        tracker.Closed.Should().ContainSingle();
        tracker.Closed[0].NetProfit.Should().Be(56);
        tracker.Closed[0].HeldDays.Should().Be(3);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ClosingPartOfAPositionRealisesOnlyThatPartAndKeepsTheCostBasis()
    {
        var ledger = new PositionLedger();

        ledger.Apply(10, 100).Should().Be(0, "opening realises nothing");
        ledger.AverageCost.Should().Be(100);

        ledger.Apply(-4, 130).Should().BeApproximately(120, 1e-12, "four shares gained 30 each");
        ledger.Shares.Should().Be(6);
        ledger.AverageCost.Should().Be(100, "what is still held was bought at 100, not at 130");
    }
}
