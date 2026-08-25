using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;

namespace TelemetryDashboard.Tests;

/// <summary>
/// ARCHITECTURE §3's actual requirement: not where a sample sits, but whether two can be ordered.
/// </summary>
/// <remarks>
/// The table in that document carried "Clock offset across nodes | Built" beside "Uncertainty on
/// that offset | Not started", which was the right split and still understated it. The offset was
/// an exponential moving average reported as a bare double, with 0.0 for a node nobody had ever
/// compared clocks with — the same "a value nobody measured, presented as a measurement" defect
/// that <c>GetAligned</c> had already been fixed for, left behind in the method beside it.
/// </remarks>
public class ClockOffsetUncertaintyTests
{
    private const string Node = "PSFB-01";

    [Fact]
    [Trait("Category", "Tier1")]
    public void ANodeNobodyHasComparedClocksWithHasNoOffsetRatherThanAnOffsetOfZero()
    {
        // Zero is the claim that two clocks agree perfectly. It is also the strongest claim this
        // type can make, and it was what an unknown node got for free.
        ClockOffsetEstimate estimate = new TimeSyncJitterBuffer().GetClockOffset(Node);

        estimate.HasOffset.Should().BeFalse();
        estimate.Samples.Should().Be(0);
        estimate.IsBounded.Should().BeFalse();
        estimate.CanOrder(3600).Should().BeFalse(
            "an hour apart is not orderable either, if nothing is known about the clocks");
        estimate.Describe().Should().Contain("not measured");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void OneObservationGivesAnOffsetAndDeliberatelyNoErrorBar()
    {
        var buffer = new TimeSyncJitterBuffer();
        buffer.SyncNodeClock(Node, masterTime: 100.0, nodeTime: 90.0);

        ClockOffsetEstimate estimate = buffer.GetClockOffset(Node);

        estimate.OffsetSec.Should().Be(10.0);
        estimate.Samples.Should().Be(1);
        estimate.SpreadSec.Should().BeNull("a spread needs two observations to exist");
        estimate.CanOrder(1000).Should().BeFalse(
            "this is the case §3 was written about -- a point estimate is read as a guarantee "
            + "precisely because nothing beside it says otherwise");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheSpreadAcrossObservationsBecomesTheErrorBar()
    {
        var buffer = new TimeSyncJitterBuffer();

        // Same true offset, three different transit delays. Every observation is offset+transit.
        buffer.SyncNodeClock(Node, masterTime: 100.20, nodeTime: 90.0);
        buffer.SyncNodeClock(Node, masterTime: 200.05, nodeTime: 190.0);
        buffer.SyncNodeClock(Node, masterTime: 300.35, nodeTime: 290.0);

        ClockOffsetEstimate estimate = buffer.GetClockOffset(Node);

        estimate.Samples.Should().Be(3);
        estimate.OffsetSec.Should().BeApproximately(10.05, 1e-9,
            "the least-delayed observation is the least overstated; the mean the EMA used to "
            + "compute would be worse by exactly the average transit");
        estimate.SpreadSec.Should().NotBeNull();
        estimate.SpreadSec!.Value.Should().BeApproximately(0.30, 1e-9);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void OrderingIsRefusedInsideTheUncertaintyAndAllowedOutsideIt()
    {
        // The question the whole section exists to make askable. Two events a millisecond apart on
        // two nodes cannot be ordered unless the offset is known to better than a millisecond.
        var buffer = new TimeSyncJitterBuffer();
        buffer.SyncNodeClock(Node, masterTime: 100.0, nodeTime: 90.0);
        buffer.SyncNodeClock(Node, masterTime: 200.1, nodeTime: 190.0);

        ClockOffsetEstimate estimate = buffer.GetClockOffset(Node);
        estimate.SpreadSec!.Value.Should().BeApproximately(0.1, 1e-9);

        // The bound is taken from the estimate rather than written as 0.1. Subtracting these
        // doubles lands a hair under a tenth, so the literal was on the far side of the boundary
        // it claimed to sit on and the case passed for the wrong reason -- readable only because
        // the strict comparison here is exact while the assertion above carries a tolerance.
        double bound = estimate.SpreadSec!.Value;

        estimate.CanOrder(0.05).Should().BeFalse("inside the error bar, and 50ms is not 'nearly'");
        estimate.CanOrder(bound).Should().BeFalse("exactly at the bound is not outside it");
        estimate.CanOrder(0.5).Should().BeTrue();
        estimate.CanOrder(-0.5).Should().BeTrue("which of the two came first is a separate question");
        estimate.CanOrder(0.0).Should().BeFalse("simultaneous readings order nothing");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void APeerSendingNonsenseCannotPoisonTheEstimate()
    {
        // §7: what is exchanged is data and it is not trusted. A NaN observation would make the
        // minimum NaN and take the node's whole timeline with it -- and a comparison against NaN
        // is false, so it would not even be caught downstream.
        var buffer = new TimeSyncJitterBuffer();
        buffer.SyncNodeClock(Node, masterTime: 100.0, nodeTime: 90.0);
        buffer.SyncNodeClock(Node, masterTime: double.NaN, nodeTime: 190.0);
        buffer.SyncNodeClock(Node, masterTime: double.PositiveInfinity, nodeTime: 290.0);

        ClockOffsetEstimate estimate = buffer.GetClockOffset(Node);

        estimate.Samples.Should().Be(1, "the two unusable readings were dropped, not recorded");
        estimate.OffsetSec.Should().Be(10.0);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheWindowSlidesSoADriftingClockIsFollowedRatherThanAveragedForever()
    {
        // An estimate over unbounded history keeps quoting a minimum that stopped being reachable
        // an hour ago, and the error bar stops covering the truth without ever getting wider.
        var buffer = new TimeSyncJitterBuffer();

        for (int i = 0; i < TimeSyncJitterBuffer.MaxClockObservations; i++)
        {
            buffer.SyncNodeClock(Node, masterTime: 100.0 + i, nodeTime: 100.0 + i - 5.0);
        }

        buffer.GetClockOffset(Node).OffsetSec.Should().Be(5.0);

        for (int i = 0; i < TimeSyncJitterBuffer.MaxClockObservations; i++)
        {
            buffer.SyncNodeClock(Node, masterTime: 500.0 + i, nodeTime: 500.0 + i - 9.0);
        }

        ClockOffsetEstimate after = buffer.GetClockOffset(Node);
        after.Samples.Should().Be(TimeSyncJitterBuffer.MaxClockObservations);
        after.OffsetSec.Should().Be(9.0, "the old window has been pushed out entirely");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ClearingForgetsTheClockAsUnmeasuredRatherThanAsZero()
    {
        var buffer = new TimeSyncJitterBuffer();
        buffer.SyncNodeClock(Node, masterTime: 100.0, nodeTime: 90.0);
        buffer.SyncNodeClock(Node, masterTime: 200.1, nodeTime: 190.0);

        buffer.ClearBuffer(Node);

        buffer.GetClockOffset(Node).HasOffset.Should().BeFalse(
            "a cleared buffer knows nothing about the clock, which is not the same as knowing "
            + "the clocks agree");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void WhatItSaysOutLoudCarriesTheCaveatItCannotResolve()
    {
        // Every observation is offset+transit, and one-way messages never separate the two. The
        // spread is therefore a floor under the uncertainty, and printing it bare would present a
        // floor as a ceiling -- the same error one level in.
        var buffer = new TimeSyncJitterBuffer();
        buffer.SyncNodeClock(Node, masterTime: 100.0, nodeTime: 90.0);
        buffer.SyncNodeClock(Node, masterTime: 200.1, nodeTime: 190.0);

        buffer.GetClockOffset(Node).Describe().Should().Contain("lower bound");
    }
}
