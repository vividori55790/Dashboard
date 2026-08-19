using FluentAssertions;
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
        double aligned = jitterBuffer.GetAlignedSample(nodeId, 15.0);
        aligned.Should().Be(150.0);
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

        double val15 = jitterBuffer.GetAlignedSample(nodeId, 15.0);
        val15.Should().Be(150.0);

        double val25 = jitterBuffer.GetAlignedSample(nodeId, 25.0);
        val25.Should().Be(250.0);
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

        double aligned = jitterBuffer.GetAlignedSample(nodeId, 20.0); // t=15+5 local + 10 offset = 20 aligned master
        aligned.Should().Be(100.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TimeSyncJitterBuffer_EmptyOrBoundary_ReturnsExpectedValues()
    {
        var jitterBuffer = new TimeSyncJitterBuffer();
        string nodeId = "MCU_4";

        jitterBuffer.GetAlignedSample(nodeId, 10.0).Should().Be(0.0);

        jitterBuffer.EnqueueSample(nodeId, 10.0, 42.0);
        jitterBuffer.GetAlignedSample(nodeId, 5.0).Should().Be(42.0);
        jitterBuffer.GetAlignedSample(nodeId, 15.0).Should().Be(42.0);
    }
}
