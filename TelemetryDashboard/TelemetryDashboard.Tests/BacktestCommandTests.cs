using TelemetryDashboard.Core.Backtesting;
using TelemetryDashboard.Core.Backtesting.Strategies;
using TelemetryDashboard.Host.Backtest;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The subcommand's command line, and the shipped sample it is meant to run out of the box.
/// </summary>
/// <remarks>
/// The sample tests read the real vendor file that ships beside the host. A backtester validated
/// only against a series invented by its own test suite has been validated against data with no
/// halts, no split, no duplicate date and no placeholder — which is to say against none of the
/// things that break readers.
/// </remarks>
public class BacktestCommandTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TelemetryDashboard.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("TelemetryDashboard.sln not found.");
    }

    private static string SamplePath(string symbol) =>
        Path.Combine(SolutionRoot(), "TelemetryDashboard.Host", "Samples", symbol + ".csv");

    // ---- the command line -----------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheVerbIsRecognisedOnlyAsTheFirstWord()
    {
        BacktestCommandLine.Matches(new[] { "backtest", "SPY" }).Should().BeTrue();
        BacktestCommandLine.Matches(new[] { "BACKTEST" }).Should().BeTrue();
        BacktestCommandLine.Matches(new[] { "--port", "8080", "backtest" }).Should().BeFalse(
            "a host serving telemetry must not be diverted into a replay by a stray word");
        BacktestCommandLine.Matches(Array.Empty<string>()).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void CostsDefaultToSomethingRatherThanToNothing()
    {
        BacktestCommandLine command = BacktestCommandLine.Parse(new[] { "backtest", "SPY" });

        command.Error.Should().BeNull();
        command.Settings.CommissionBps.Should().BeGreaterThan(0);
        command.Settings.SlippageBps.Should().BeGreaterThan(0);
        command.Settings.Field.Should().Be(PriceField.AdjustedClose,
            "the raw close shows a split as a crash that never happened");
    }

    [Theory]
    [Trait("Category", "Tier2")]
    [InlineData(new[] { "backtest" }, "price file is required")]
    [InlineData(new[] { "backtest", "SPY", "--nonsense" }, "unknown argument")]
    [InlineData(new[] { "backtest", "SPY", "--fast" }, "needs a value")]
    [InlineData(new[] { "backtest", "SPY", "--fast", "twenty" }, "needs a value")]
    [InlineData(new[] { "backtest", "SPY", "AAPL" }, "second file")]
    [InlineData(new[] { "backtest", "SPY", "--price", "midpoint" }, "--price accepts")]
    [InlineData(new[] { "backtest", "SPY", "--from", "yesterday" }, "yyyy-MM-dd")]
    [InlineData(new[] { "backtest", "SPY", "--from", "2024-06-01", "--to", "2024-01-01" }, "after --to")]
    [InlineData(new[] { "backtest", "SPY", "--cash", "-100" }, "starting cash must be positive")]
    [InlineData(new[] { "backtest", "SPY", "--commission-bps", "-1" }, "commission cannot be negative")]
    public void AMistypedCommandLineIsRefusedWithTheReason(string[] args, string expected)
    {
        BacktestCommandLine.Parse(args).Error.Should().Contain(expected);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AFlagThatWasGivenIsTheFlagThatIsUsed()
    {
        BacktestCommandLine command = BacktestCommandLine.Parse(new[]
        {
            "backtest", "AAPL", "--strategy", "mean-reversion", "--window", "30",
            "--entry-z", "2.5", "--exit-z", "0.25", "--short",
            "--cash", "50000", "--commission-bps", "0", "--slippage-bps", "0",
            "--price", "close", "--from", "2020-01-01", "--to", "2021-12-31"
        });

        command.Error.Should().BeNull();
        command.Strategy.Should().Be("mean-reversion");
        command.Options.Window.Should().Be(30);
        command.Options.EntryZ.Should().Be(2.5);
        command.Options.ExitZ.Should().Be(0.25);
        command.Options.AllowShort.Should().BeTrue();
        command.Settings.StartingCash.Should().Be(50_000);
        command.Settings.CommissionBps.Should().Be(0, "zero is a choice, and only refused as a default");
        command.Settings.Field.Should().Be(PriceField.Close);
        command.From.Should().Be(new DateOnly(2020, 1, 1));
        command.To.Should().Be(new DateOnly(2021, 12, 31));
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheHelpScreenNamesEveryStrategyTheCatalogueCanBuild()
    {
        string help = BacktestUsageText.Render();

        foreach (string name in StrategyCatalogue.Descriptions.Keys) help.Should().Contain(name);
        help.Should().Contain("NEXT session's open", "the one property that makes the output worth reading");
    }

    // ---- the shipped sample, which is real market data -------------------------

    [Theory]
    [Trait("Category", "Tier1")]
    [InlineData("SPY")]
    [InlineData("AAPL")]
    [InlineData("KO")]
    public void TheShippedSampleIsAVendorExportThisReaderParsesWithNothingDiscarded(string symbol)
    {
        PriceCsvLoad load = PriceCsvReader.ReadFile(SamplePath(symbol));

        load.Error.Should().BeNull();
        load.Series!.Count.Should().BeGreaterThan(2000, "ten years of daily bars");
        load.HasAdjustedClose.Should().BeTrue();
        load.Discarded.Should().Be(0, "if the vendor file grows a bad row, this is where it surfaces");
        load.Series.FirstDate.Should().BeBefore(load.Series.LastDate);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void OnRealDataAFrictionlessHoldEarnsExactlyWhatThePricesDid()
    {
        // The anchor. Buy-and-hold with no costs must return precisely the ratio of the last
        // adjusted close to the adjusted open of the session the entry filled in -- which is the
        // second session, because the decision was made when the first one closed. Any look-ahead,
        // any double-counted bar, any drift in the position arithmetic breaks this identity, and it
        // is computed here from the file rather than from anything the engine produced.
        BarSeries series = PriceCsvReader.ReadFile(SamplePath("SPY")).Series!;
        var settings = new BacktestSettings
        {
            StartingCash = 10_000, CommissionBps = 0, SlippageBps = 0, Field = PriceField.AdjustedClose
        };

        BacktestResult held = new BacktestEngine(settings).Run(series, new BuyAndHoldStrategy());

        double entry = series[1].OpenOf(PriceField.AdjustedClose);
        double exit = series[^1].PriceOf(PriceField.AdjustedClose);

        held.FinalEquity.Should().BeApproximately(10_000 * (exit / entry), 1e-6);
        held.Fills.Should().ContainSingle("holding is one trade, whatever the weight is asked for every bar");
        held.EndedWithOpenPosition.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void OnRealDataEveryFillIsPricedAtThatSessionsOpenAndNeverItsClose()
    {
        // The same guarantee as the unit test, asserted against ten years of real sessions where
        // the open and the close differ on nearly every one of them.
        BarSeries series = PriceCsvReader.ReadFile(SamplePath("SPY")).Series!;
        var opens = series.Bars.ToDictionary(b => b.Date, b => b.OpenOf(PriceField.AdjustedClose));

        BacktestResult run = new BacktestEngine(new BacktestSettings())
            .Run(series, new MovingAverageCrossStrategy(50, 200));

        run.Fills.Should().NotBeEmpty();
        foreach (TradeFill fill in run.Fills)
        {
            fill.ReferencePrice.Should().BeApproximately(opens[fill.Date], 1e-9,
                $"the fill on {fill.Date:yyyy-MM-dd} must be at that session's open");
        }
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void OnRealDataCostsMakeTheSameStrategyStrictlyWorse()
    {
        // A frictionless backtest flatters exactly the rules that trade most. This is that claim
        // made checkable: the identical rule over the identical sessions, differing only in what it
        // was charged.
        BarSeries series = PriceCsvReader.ReadFile(SamplePath("KO")).Series!;
        var free = new BacktestSettings { CommissionBps = 0, SlippageBps = 0 };
        var charged = new BacktestSettings { CommissionBps = 5, SlippageBps = 2 };

        BacktestResult a = new BacktestEngine(free).Run(series, new MovingAverageCrossStrategy(20, 100));
        BacktestResult b = new BacktestEngine(charged).Run(series, new MovingAverageCrossStrategy(20, 100));

        a.CommissionPaid.Should().Be(0);
        b.CommissionPaid.Should().BeGreaterThan(0);
        b.FinalEquity.Should().BeLessThan(a.FinalEquity);
        b.RoundTrips.Should().NotBeEmpty("a rule with no closed trips would prove nothing here");
    }
}
