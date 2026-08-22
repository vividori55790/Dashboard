using TelemetryDashboard.Core.Backtesting;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Reading the files market-data vendors actually write, including the rows they get wrong.
/// </summary>
/// <remarks>
/// Every case here is one a decade of real daily bars contains at least once. A reader that assumes
/// a clean file does not fail on a dirty one — it produces a price of zero, a session in the wrong
/// order, or a share count where an adjusted close belongs, and then the backtest above it reports
/// a confident number.
/// </remarks>
public class PriceCsvReaderTests
{
    private const string YahooHeader = "Date,Open,High,Low,Close,Adj Close,Volume";

    [Fact]
    [Trait("Category", "Tier1")]
    public void ReadsTheLayoutYahooExports()
    {
        PriceCsvLoad load = PriceCsvReader.Read(new[]
        {
            YahooHeader,
            "2024-01-02,100.5,101.0,99.5,100.0,95.0,1000",
            "2024-01-03,100.0,102.0,100.0,101.5,96.4,1200"
        }, "TEST");

        load.Error.Should().BeNull();
        load.Series!.Count.Should().Be(2);
        load.HasAdjustedClose.Should().BeTrue();
        load.Series[0].AdjustedClose.Should().Be(95.0);
        load.Series[0].Volume.Should().Be(1000);
        load.Discarded.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ReadsAVendorFileWithNoAdjustedCloseAndSaysWhatThatCosts()
    {
        // Stooq's layout. Read by column position rather than by name, the volume would land in
        // the adjusted close and a decade of equity would be marked against share counts.
        PriceCsvLoad load = PriceCsvReader.Read(new[]
        {
            "Date,Open,High,Low,Close,Volume",
            "2024-01-02,100.5,101.0,99.5,100.0,5000000"
        }, "TEST");

        load.Series!.Count.Should().Be(1);
        load.HasAdjustedClose.Should().BeFalse();
        load.Series[0].AdjustedClose.Should().Be(100.0, "falls back to the close, not to the volume");
        load.Notes().Should().Contain(n => n.Contains("splits and dividends", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void RowsInDescendingOrderAreSortedRatherThanRunBackwards()
    {
        // Several vendors export newest-first. Run as given, a strategy walks history backwards and
        // reports a plausible equity curve for a trade sequence that could not have happened.
        PriceCsvLoad load = PriceCsvReader.Read(new[]
        {
            YahooHeader,
            "2024-01-04,102,103,101,102.5,102.5,10",
            "2024-01-03,101,102,100,101.5,101.5,10",
            "2024-01-02,100,101,99,100.5,100.5,10"
        }, "TEST");

        load.Series!.FirstDate.Should().Be(new DateOnly(2024, 1, 2));
        load.Series.LastDate.Should().Be(new DateOnly(2024, 1, 4));
    }

    [Theory]
    [Trait("Category", "Tier2")]
    [InlineData("2024-01-03,0,0,0,0,0,0", "a halted session written as zeros")]
    [InlineData("2024-01-03,100,99,101,100,100,10", "a high below the low")]
    [InlineData("2024-01-03,100,101,99,105,105,10", "a close above the high")]
    [InlineData("2024-01-03,-5,101,99,100,100,10", "a negative price")]
    public void ASessionNoMarketCouldHaveTradedIsDroppedAndCounted(string row, string why)
    {
        PriceCsvLoad load = PriceCsvReader.Read(new[]
        {
            YahooHeader,
            "2024-01-02,100,101,99,100.5,100.5,10",
            row
        }, "TEST");

        load.Series!.Count.Should().Be(1, why);
        load.IncoherentRows.Should().Be(1);
        load.Notes().Should().Contain(n => n.Contains("impossible session", StringComparison.Ordinal),
            "a silently dropped row is a gap the reader of the equity curve cannot see");
    }

    [Theory]
    [Trait("Category", "Tier2")]
    [InlineData("null")]
    [InlineData("N/A")]
    [InlineData("")]
    public void APlaceholderForAnUnknownPriceIsNotReadAsZero(string placeholder)
    {
        // Every one of these parses to zero under a lenient reader, and a zero price is a total
        // loss the market never delivered.
        PriceCsvLoad load = PriceCsvReader.Read(new[]
        {
            YahooHeader,
            "2024-01-02,100,101,99,100.5,100.5,10",
            $"2024-01-03,{placeholder},101,99,100,100,10"
        }, "TEST");

        load.Series!.Count.Should().Be(1);
        load.UnparseableRows.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AByteOrderMarkOnTheHeaderDoesNotHideTheDateColumn()
    {
        PriceCsvLoad load = PriceCsvReader.Read(new[]
        {
            "﻿" + YahooHeader,
            "2024-01-02,100,101,99,100.5,100.5,10"
        }, "TEST");

        load.Error.Should().BeNull("a spreadsheet export leaves the mark on the first cell");
        load.Series!.Count.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ADuplicatedDateIsDroppedAndReportedRatherThanCountedTwice()
    {
        PriceCsvLoad load = PriceCsvReader.Read(new[]
        {
            YahooHeader,
            "2024-01-02,100,101,99,100.5,100.5,10",
            // The high is raised to hold the corrected close. Written as 101 while the close said
            // 111, this row is an impossible session and the coherence check drops it there --
            // which is how the first version of this test came to assert a duplicate that the
            // reader never saw.
            "2024-01-02,100,112,99,111.0,111.0,10"
        }, "TEST");

        load.Series!.Count.Should().Be(1);
        load.IncoherentRows.Should().Be(0);
        load.DuplicateDates.Should().Be(1);
        load.Series[0].Close.Should().Be(111.0, "the last row read wins, as a corrections file expects");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ATimestampWithAClockPartIsStillOneSession()
    {
        PriceCsvLoad load = PriceCsvReader.Read(new[]
        {
            YahooHeader,
            "2024-01-02 00:00:00,100,101,99,100.5,100.5,10",
            "2024-01-03T00:00:00Z,101,102,100,101.5,101.5,10"
        }, "TEST");

        load.Series!.Count.Should().Be(2);
        load.Series[1].Date.Should().Be(new DateOnly(2024, 1, 3));
    }

    [Theory]
    [Trait("Category", "Tier2")]
    [InlineData("Open,High,Low,Close", "no Date column")]
    [InlineData("Date,Open,Close", "missing required column")]
    public void AFileThatIsNotAPriceExportIsRefusedWithTheReason(string header, string expected)
    {
        PriceCsvLoad load = PriceCsvReader.Read(new[] { header, "1,2,3,4" }, "TEST");

        load.Series.Should().BeNull();
        load.Error.Should().Contain(expected);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AMissingFileIsAnAnswerRatherThanAnException()
    {
        PriceCsvReader.ReadFile(Path.Combine(Path.GetTempPath(), "no-such-symbol-9f3a.csv"))
            .Error.Should().Contain("no such file");
    }
}
