using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Backtesting;
using TelemetryDashboard.Core.Backtesting.Strategies;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The rules themselves, and the parameter combinations they refuse.
/// </summary>
public class BacktestStrategyTests
{
    private static StrategyContext ContextOver(params double[] closes)
    {
        BarSeries series = BarSeries.Create("TEST", closes.Select((c, i) => new PriceBar
        {
            Date = new DateOnly(2024, 1, 1).AddDays(i),
            Open = c, High = c, Low = c, Close = c, AdjustedClose = c
        }), out _);

        return new StrategyContext(series, closes.Length - 1, PriceField.Close, 0);
    }

    private static double? FeedAll(IBarStrategy strategy, params double[] closes)
    {
        BarSeries series = BarSeries.Create("TEST", closes.Select((c, i) => new PriceBar
        {
            Date = new DateOnly(2024, 1, 1).AddDays(i),
            Open = c, High = c, Low = c, Close = c, AdjustedClose = c
        }), out _);

        strategy.Reset();
        double? last = null;
        for (int i = 0; i < series.Count; i++)
        {
            last = strategy.Decide(new StrategyContext(series, i, PriceField.Close, 0));
        }
        return last;
    }

    // ---- the moving average ---------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void AMovingAverageIsTheMeanOfTheLastNValuesIncludingTheNewest()
    {
        // The distinction this class exists for. RollingChannelStatistics.Mean deliberately
        // excludes the newest sample so a spike cannot inflate the deviation it is measured
        // against -- correct for a z-score, and a one-bar-lagged average for a crossover, which
        // would shift every signal by a session while looking entirely reasonable.
        var average = new MovingAverage(3);
        var statistics = new RollingChannelStatistics(3);

        foreach (double v in new[] { 10.0, 20.0, 30.0 })
        {
            average.Add(v);
            statistics.Add(v);
        }

        average.Value.Should().Be(20.0, "(10 + 20 + 30) / 3");
        statistics.Mean.Should().Be(15.0, "(10 + 20) / 2 -- the baseline excludes the newest on purpose");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AnAverageIsNotReadyUntilItsWindowSpansItsWholePeriod()
    {
        var average = new MovingAverage(4);
        average.Add(1);
        average.Add(2);

        average.IsReady.Should().BeFalse();
        average.Value.Should().Be(1.5, "it still answers, but the answer is over two values not four");

        average.Add(3);
        average.Add(4);
        average.IsReady.Should().BeTrue();

        average.Add(5);
        average.Value.Should().Be(3.5, "(2 + 3 + 4 + 5) / 4 -- the oldest was evicted");
    }

    // ---- the crossover --------------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void TwoAveragesOfTheSameLengthNeverCrossSoTheRuleIsRefused()
    {
        // Not a degenerate edge case worth tolerating: it holds whatever its first bar happened to
        // say, forever, and reports a result for a strategy nobody wrote.
        FluentActions.Invoking(() => new MovingAverageCrossStrategy(50, 50))
            .Should().Throw<ArgumentOutOfRangeException>().WithMessage("*never cross*");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheCrossoverSaysNothingUntilItsLongAverageIsFull()
    {
        // Before that, the long average is over however many bars have arrived -- a short average
        // by another name -- so the two would cross on nothing but their differing warm-up
        // lengths, and every run's first trade would be an artefact of where the file starts.
        var strategy = new MovingAverageCrossStrategy(2, 5);

        FeedAll(strategy, 1, 2, 3, 4).Should().BeNull();
        FeedAll(strategy, 1, 2, 3, 4, 5).Should().Be(1.0, "rising, so the fast average is above the slow");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ADowntrendStandsAsideUnlessShortingWasAskedFor()
    {
        double[] falling = { 10, 9, 8, 7, 6, 5, 4, 3 };

        FeedAll(new MovingAverageCrossStrategy(2, 5), falling).Should().Be(0.0);
        FeedAll(new MovingAverageCrossStrategy(2, 5, allowShort: true), falling).Should().Be(-1.0);
    }

    // ---- mean reversion -------------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void AnEntryThresholdInsideTheExitThresholdIsRefused()
    {
        // With them the wrong way round the rule opens and closes on the same bar, and pays
        // commission for the privilege on every one of them.
        FluentActions.Invoking(() => new MeanReversionStrategy(20, entryZ: 0.5, exitZ: 2.0))
            .Should().Throw<ArgumentOutOfRangeException>().WithMessage("*same bar*");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ItBuysAPriceFarBelowItsOwnRecentMeanAndNotOneFarAbove()
    {
        double[] quiet = { 100, 100.2, 99.8, 100.1, 99.9, 100.0, 100.1, 99.9, 100.2, 99.8 };

        FeedAll(new MeanReversionStrategy(window: 10, entryZ: 2.0), quiet.Append(90.0).ToArray())
            .Should().Be(1.0, "a fall of ten points against that baseline is well past two sigma");

        FeedAll(new MeanReversionStrategy(window: 10, entryZ: 2.0), quiet.Append(110.0).ToArray())
            .Should().BeNull("as far out, but upward -- and shorting was not asked for");

        FeedAll(new MeanReversionStrategy(window: 10, entryZ: 2.0, allowShort: true), quiet.Append(110.0).ToArray())
            .Should().Be(-1.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AChannelThatNeverMovedOpensNoPosition()
    {
        // A window with no standard deviation has no scale to judge against. The statistics class
        // answers zero rather than dividing by nothing, which reads as sitting on the mean -- and
        // that is what a flat series is.
        FeedAll(new MeanReversionStrategy(window: 5), 50, 50, 50, 50, 50, 50)
            .Should().BeNull();
    }

    // ---- the catalogue --------------------------------------------------------

    [Theory]
    [Trait("Category", "Tier2")]
    [InlineData("sma-cross")]
    [InlineData("sma")]
    [InlineData("mean-reversion")]
    [InlineData("MEAN-REVERSION")]
    [InlineData("buy-and-hold")]
    public void EveryNameTheHelpScreenOffersBuildsSomething(string name)
    {
        StrategyCatalogue.TryCreate(name, new StrategyOptions(), out IBarStrategy? strategy, out string? error)
            .Should().BeTrue(error);
        strategy.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AnUnknownNameListsTheKnownOnesRatherThanFallingBackToADefault()
    {
        StrategyCatalogue.TryCreate("momentum", new StrategyOptions(), out IBarStrategy? strategy, out string? error)
            .Should().BeFalse();

        strategy.Should().BeNull("silently running a different rule than the one asked for is the worst outcome");
        error.Should().Contain("sma-cross").And.Contain("mean-reversion");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ARefusedParameterCombinationReadsAsATypoNotAStackTrace()
    {
        StrategyCatalogue.TryCreate("sma-cross", new StrategyOptions { Fast = 200, Slow = 50 },
            out _, out string? error).Should().BeFalse();

        error.Should().Contain("never cross");
        error.Should().NotContain("Parameter", "the framework's suffix names an argument nobody typed");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheBenchmarkNameTheReportPrintsIsOneTheCatalogueCanBuild()
    {
        // They drifted apart trivially easily: the report labels its second column from the
        // constant, and a person reading it types that word into --strategy.
        StrategyCatalogue.Descriptions.Should().ContainKey(StrategyCatalogue.Benchmark);
        StrategyCatalogue.TryCreate(StrategyCatalogue.Benchmark, new StrategyOptions(), out _, out _)
            .Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void EveryStrategyDeclaresTheWarmUpItActuallyNeeds()
    {
        new MovingAverageCrossStrategy(50, 200).WarmUpBars.Should().Be(200);
        new MeanReversionStrategy(20).WarmUpBars.Should().Be(20);
        new BuyAndHoldStrategy().WarmUpBars.Should().Be(0);

        // Declared rather than inferred so the report can say how much of the file was spent
        // deciding nothing -- a 200-day average over a one-year file spends four months of it.
        ContextOver(1, 2, 3).Count.Should().Be(3);
    }
}
