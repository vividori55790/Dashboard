using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Collections;
using TelemetryDashboard.Core.Services;
using Xunit;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F01_RingBufferTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void RingBuffer_EnqueueAndDequeue_FirstInFirstOutOrder()
    {
        var buffer = new RingBuffer<string>(5);
        buffer.Enqueue("A");
        buffer.Enqueue("B");
        buffer.Enqueue("C");

        buffer.Count.Should().Be(3);
        buffer.IsFull.Should().BeFalse();
        buffer.IsEmpty.Should().BeFalse();

        buffer.TryDequeue(out var item1).Should().BeTrue();
        item1.Should().Be("A");

        buffer.TryDequeue(out var item2).Should().BeTrue();
        item2.Should().Be("B");

        buffer.Count.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void RingBuffer_Overflow_DropsOldestItem()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.Enqueue(10);
        buffer.Enqueue(20);
        buffer.Enqueue(30);
        buffer.IsFull.Should().BeTrue();

        // Enqueueing 4th item overwrites oldest (10)
        buffer.Enqueue(40);
        buffer.Count.Should().Be(3);

        var flushed = buffer.Flush();
        flushed.Should().Equal(20, 30, 40);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void RingBuffer_Flush_EmptiesBufferAndReturnsItems()
    {
        var buffer = new RingBuffer<double>(10);
        buffer.Enqueue(1.1);
        buffer.Enqueue(2.2);
        buffer.Enqueue(3.3);

        var items = buffer.Flush();
        items.Should().Equal(1.1, 2.2, 3.3);
        buffer.IsEmpty.Should().BeTrue();
        buffer.Count.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void RingBuffer_Clear_ResetsBuffer()
    {
        var buffer = new RingBuffer<int>(5);
        buffer.Enqueue(1);
        buffer.Enqueue(2);
        buffer.Clear();

        buffer.IsEmpty.Should().BeTrue();
        buffer.Count.Should().Be(0);
        buffer.TryDequeue(out _).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ZeroLossPacketBuffer_ReplaysEveryPacketBufferedWhileOffline()
    {
        var buffer = new TelemetryDashboard.Infrastructure.Serial.ZeroLossPacketBuffer();
        buffer.OnConnectionLost();
        buffer.IsConnected.Should().BeFalse();

        buffer.BufferPacketDuringDisconnect("pkt1");
        buffer.BufferPacketDuringDisconnect("pkt2");
        buffer.PendingCount.Should().Be(2);

        var received = new List<object>();
        buffer.FlushBufferedPackets(pkt => received.Add(pkt));

        received.Should().Equal("pkt1", "pkt2");
        buffer.PendingCount.Should().Be(0);
        buffer.DroppedCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ZeroLossPacketBuffer_ReportsLossWhenOutageOutlastsCapacity()
    {
        var buffer = new TelemetryDashboard.Infrastructure.Serial.ZeroLossPacketBuffer(capacity: 4);
        buffer.OnConnectionLost();

        for (int i = 0; i < 10; i++) buffer.BufferPacketDuringDisconnect($"pkt{i}");

        // An outage longer than the buffer is real data loss; it must be counted, not hidden.
        buffer.DroppedCount.Should().Be(6);
        buffer.PendingCount.Should().Be(4);

        var received = new List<object>();
        buffer.FlushBufferedPackets(pkt => received.Add(pkt));
        received.Should().Equal("pkt6", "pkt7", "pkt8", "pkt9");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AutoReconnectEngine_OwnsTheOfflineBuffer()
    {
        var mockSerial = new Moq.Mock<TelemetryDashboard.Core.Interfaces.ISerialManager>();
        var engine = new TelemetryDashboard.Infrastructure.Serial.AutoReconnectEngine(mockSerial.Object);

        // Link state and the zero-loss buffer live together now.
        engine.OfflineBuffer.Should().NotBeNull();
        engine.OfflineBuffer.IsConnected.Should().BeTrue();
    }
}
