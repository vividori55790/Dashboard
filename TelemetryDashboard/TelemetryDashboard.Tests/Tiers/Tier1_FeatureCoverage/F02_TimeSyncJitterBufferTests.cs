using FluentAssertions;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using Xunit;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F02_TimeSyncJitterBufferTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void TimeSyncJitterBuffer_LinearInterpolation_CalculatesIntermediateValue()
    {
        var jitterBuffer = new TimeSyncJitterBuffer();
        string nodeId = "MCU_1";

        // Enqueue bounding samples (t0=10.0, v0=100.0) and (t1=20.0, v1=200.0)
        jitterBuffer.EnqueueSample(nodeId, 10.0, 100.0);
        jitterBuffer.EnqueueSample(nodeId, 20.0, 200.0);

        // Query aligned sample at masterTimestamp = 15.0
        AlignedSample aligned = jitterBuffer.GetAligned(nodeId, 15.0);

        aligned.Value.Should().Be(150.0);
        aligned.Kind.Should().Be(AlignmentKind.Interpolated,
            "150 is a value nothing reported; a caller plotting it beside measurements has to be "
            + "able to tell which is which");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TimeSyncJitterBuffer_OutofOrderEnqueue_SortsByTimestamp()
    {
        var jitterBuffer = new TimeSyncJitterBuffer();
        string nodeId = "MCU_2";

        // Enqueue out of order
        jitterBuffer.EnqueueSample(nodeId, 30.0, 300.0);
        jitterBuffer.EnqueueSample(nodeId, 10.0, 100.0);
        jitterBuffer.EnqueueSample(nodeId, 20.0, 200.0);

        jitterBuffer.GetAligned(nodeId, 15.0).Value.Should().Be(150.0);
        jitterBuffer.GetAligned(nodeId, 25.0).Value.Should().Be(250.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TimeSyncJitterBuffer_ClockDriftSync_AdjustsOffsetUsingEma()
    {
        var jitterBuffer = new TimeSyncJitterBuffer();
        string nodeId = "MCU_3";

        // masterTime=100, nodeTime=90 -> offset=10
        jitterBuffer.SyncNodeClock(nodeId, 100.0, 90.0);
        double offset = jitterBuffer.GetClockOffset(nodeId);
        offset.Should().Be(10.0);

        // Enqueue sample at local timestamp 5.0 -> aligned timestamp should be 15.0
        jitterBuffer.EnqueueSample(nodeId, 5.0, 50.0);
        jitterBuffer.EnqueueSample(nodeId, 15.0, 150.0);

        // t=15+5 local + 10 offset = 20 aligned master
        jitterBuffer.GetAligned(nodeId, 20.0).Value.Should().Be(100.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ANodeThatHasSentNothingIsNotANodeReadingZero()
    {
        // This asserted `.Should().Be(0.0)`, which pinned the defect as the requirement: a node
        // nobody had heard from and a node reading zero volts produced the same answer, and a
        // caller plotting it drew a flat line through a gap.
        var jitterBuffer = new TimeSyncJitterBuffer();

        AlignedSample nothing = jitterBuffer.GetAligned("MCU_4", 10.0);

        nothing.Kind.Should().Be(AlignmentKind.None);
        nothing.HasValue.Should().BeFalse();
        double.IsNaN(nothing.Value).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AValueHeldFromOutsideTheBufferSaysSoAndSaysHowFar()
    {
        // The old assertions were that a single sample at t=10 answers 42 for t=5 and t=15 alike.
        // It still does -- there is nothing else it could answer -- but the answer now carries the
        // fact that it describes a different instant, and by how much. An hour past the last
        // sample is the same clamp as one second past it, and only one of those is usable.
        var jitterBuffer = new TimeSyncJitterBuffer();
        jitterBuffer.EnqueueSample("MCU_4", 10.0, 42.0);

        AlignedSample before = jitterBuffer.GetAligned("MCU_4", 5.0);
        before.Value.Should().Be(42.0);
        before.Kind.Should().Be(AlignmentKind.HeldBefore);
        before.GapSec.Should().Be(5.0);
        before.AnswersTheInstant.Should().BeFalse();

        AlignedSample after = jitterBuffer.GetAligned("MCU_4", 15.0);
        after.Value.Should().Be(42.0);
        after.Kind.Should().Be(AlignmentKind.HeldAfter);
        after.GapSec.Should().Be(5.0);
        after.AnswersTheInstant.Should().BeFalse();

        AlignedSample exact = jitterBuffer.GetAligned("MCU_4", 10.0);
        exact.Kind.Should().Be(AlignmentKind.Exact);
        exact.AnswersTheInstant.Should().BeTrue();
    }
}
