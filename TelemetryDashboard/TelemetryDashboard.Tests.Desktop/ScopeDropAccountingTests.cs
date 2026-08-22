using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.UI.Controls;
using Xunit;

namespace TelemetryDashboard.Tests.Desktop;

/// <summary>
/// What the scope threw away, and the fact that it now says so.
/// </summary>
/// <remarks>
/// The scope discarded samples down three separate paths and counted none of them, so a chart
/// missing data looked exactly like a chart that was not. That is the failure this product names
/// everywhere else — <c>IngestRateGuard</c>'s own remarks say dropping is announced rather than
/// done silently — and the panel an operator watches most was the one place doing the opposite.
/// <para>
/// Measured on the running application with a twenty-channel profile against a sixteen-channel
/// plot: "Samples: 2,176 | Channels: 16 | Time: 33.8s | dropped 544 (544 past channel cap)".
/// 544 of 2,720 is exactly the four channels of twenty that never got drawn. Before this the same
/// run read "Samples: 2,176 | Channels: 16 | Time: 33.8s" and nothing on screen said that four
/// channels were missing entirely.
/// </para>
/// </remarks>
public class ScopeDropAccountingTests
{
    [WpfFact]
    [Trait("Category", "Tier1")]
    public void AHealthyRunSaysNothingRatherThanSayingZero()
    {
        // A counter permanently on screen stops being read. One that appears only when it has
        // something to say is a change an eye catches.
        new ScopeDropTally().Summary().Should().BeEmpty();
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void EachKindOfLossIsNamedSeparatelyBecauseTheyMeanDifferentThings()
    {
        var tally = new ScopeDropTally();
        tally.CountBeyondChannelCap();
        tally.CountNonFinite();
        tally.CountNonFinite();
        tally.CountOverflowed();

        string summary = tally.Summary();

        summary.Should().Contain("dropped 4");
        summary.Should().Contain("1 past channel cap", "a whole channel is missing, not a sample");
        summary.Should().Contain("2 not a number", "the device is talking nonsense, not staying quiet");
        summary.Should().Contain("1 queue overflow", "the reader could not keep up");
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void SamplesArrivingWhilePausedAreCountedApartFromTheLossesNobodyAskedFor()
    {
        // The operator asked for this one. Folding it in with the rest would make every pause look
        // like a fault, and an alarm that cries wolf on a deliberate act is worse than none.
        var tally = new ScopeDropTally();
        tally.CountWhilePaused();
        tally.CountWhilePaused();

        tally.WhilePaused.Should().Be(2);
        tally.Unintended.Should().Be(0);
        tally.Summary().Should().BeEmpty();
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void ClearingThePlotForgetsWhatWasLostBeforeIt()
    {
        var tally = new ScopeDropTally();
        tally.CountNonFinite();

        tally.Reset();

        tally.Unintended.Should().Be(0);
        tally.Summary().Should().BeEmpty();
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void CountingIsSafeFromTheThreadThatPushedTheSample()
    {
        // The non-finite path runs on whichever thread pushed the sample, while the rest run on the
        // dispatcher, so the counters are the one part of this that two threads touch.
        var tally = new ScopeDropTally();

        Parallel.For(0, 1000, _ => tally.CountNonFinite());

        tally.NonFinite.Should().Be(1000);
    }

    // ---- how many channels are drawn, and how many are held ------------------

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void ChannelsPastTheDrawingBudgetStillExistAndCanBeTicked()
    {
        // The defect this replaced. One cap of sixteen refused to *create* the seventeenth channel
        // a rig reported, so it never reached the toggle list -- the operator could not tick it,
        // untick something else, or learn it existed. The panel whose job is to show channels was
        // hiding them, and derived channels make a twenty- or thirty-channel rig ordinary.
        ScopeChannelBudget.StartsVisible(ScopeChannelBudget.DefaultPlotted - 1).Should().BeTrue();
        ScopeChannelBudget.StartsVisible(ScopeChannelBudget.DefaultPlotted).Should().BeFalse(
            "past the budget it arrives unticked, not absent");

        ScopeChannelBudget.HasRoom(ScopeChannelBudget.DefaultPlotted).Should().BeTrue(
            "the budget is about drawing, not about holding");
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void ASourceInventingChannelNamesIsStillStoppedSomewhere()
    {
        // The ceiling is a different question from the budget and still needed: a malformed parse,
        // or a device putting a serial number in the variable field, emits a fresh channel name per
        // packet. Far above any real rig, and what is refused past it is counted rather than
        // dropped in silence.
        ScopeChannelBudget.HasRoom(ScopeChannelBudget.Ceiling - 1).Should().BeTrue();
        ScopeChannelBudget.HasRoom(ScopeChannelBudget.Ceiling).Should().BeFalse();
        ScopeChannelBudget.Ceiling.Should().BeGreaterThan(ScopeChannelBudget.DefaultPlotted * 4);
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void AChannelIsDrawnByDefaultSoTheBudgetIsWhatWithholdsIt()
    {
        // If a series defaulted to hidden, every channel would need a tick before anything appeared
        // and the budget would be doing nothing.
        new ScopeChannelSeries("TEMP", index: 0).IsVisible.Should().BeTrue();
    }

    // ---- the buffer behaviours the deleted view model used to stand in for ----

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void AChannelKeepsAWindowRatherThanGrowingWithoutBound()
    {
        // F17_Boundary_ExtremePointCount_100kPoints_NoBufferOverflow asked this of ScopeViewModel,
        // which nothing constructed. ScopeChannelSeries is what the running scope actually uses.
        var series = new ScopeChannelSeries("VIB", index: 0, capacity: 400);

        for (int i = 0; i < 100_000; i++) series.Add(i * 0.01, 42.0);

        series.SampleCount.Should().Be(400);
        series.Snapshot().Ys.Should().HaveCount(400);
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void AChannelWithNoSamplesHasNothingToDraw()
    {
        var series = new ScopeChannelSeries("TEMP", index: 0);

        series.SampleCount.Should().Be(0);
        series.Snapshot().Xs.Should().BeEmpty();
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void ClearingASeriesEmptiesBothAxesTogether()
    {
        // The two queues are stepped in lockstep; emptying one and not the other would misalign
        // every remaining point against its timestamp.
        var series = new ScopeChannelSeries("TEMP", index: 0);
        for (int i = 0; i < 50; i++) series.Add(i, i * 2.0);

        series.Clear();

        (double[] xs, double[] ys) = series.Snapshot();
        xs.Should().BeEmpty();
        ys.Should().BeEmpty();
        series.SampleCount.Should().Be(0);
    }
}
