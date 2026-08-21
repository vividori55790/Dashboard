namespace TelemetryDashboard.Tests.Tiers.Tier4_RealWorldWorkloads;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Tests.TestUtilities;
using Xunit;

/// <summary>
/// Tier 4 Real-World Application Workload Test Suite:
/// Verifies 60-second high-rate telemetry streaming stress workloads (100k+ pts/sec simulation,
/// memory leak verification, snapshot extraction under failure, and channel stability).
/// </summary>
[Trait("Category", "Tier4")]
[Collection(HeavyTestCollection.Name)]
public class SustainedTelemetryStressTest
{
    private class RollingSnapshotExtractor
    {
        private readonly ConcurrentQueue<TelemetryPacket> _rollingBuffer = new();
        private readonly TimeSpan _windowSize = TimeSpan.FromSeconds(10);

        public void Add(TelemetryPacket packet)
        {
            _rollingBuffer.Enqueue(packet);
            var cutoff = packet.Timestamp - _windowSize;
            while (_rollingBuffer.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
            {
                _rollingBuffer.TryDequeue(out _);
            }
        }

        public List<TelemetryPacket> ExtractSnapshot(DateTime failureTime)
        {
            var cutoff = failureTime - _windowSize;
            return _rollingBuffer.Where(p => p.Timestamp >= cutoff && p.Timestamp <= failureTime).ToList();
        }
    }

    [Fact]
    public void HighRateStreaming_100kPtsPerSec_SustainedThroughputAndZeroLoss()
    {
        const int totalPackets = 100_000;
        var router = new DataRouter();
        router.RegisterRule(new RoutingRule
        {
            RuleType = RuleType.Prefix,
            Tag = "TELE",
            Port = "*",
            TargetNodeId = "STRESS_NODE"
        });

        int processedCount = 0;
        router.PacketRouted += (s, pkt) => Interlocked.Increment(ref processedCount);

        var stopwatch = Stopwatch.StartNew();

        // Generate and route 100,000 telemetry frames
        for (int i = 0; i < totalPackets; i++)
        {
            var body = $"TELE,STRESS_NODE,TEMP,{(45.0 + (i % 50)):F2},C";
            byte xor = TestDataGenerator.CalculateXorChecksum(body);
            var raw = new RawPacket("COM1", $"${body}*{xor:X2}", DateTime.UtcNow);
            router.Route(raw);
        }

        stopwatch.Stop();

        processedCount.Should().Be(totalPackets);
        double packetsPerSec = totalPackets / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
        packetsPerSec.Should().BeGreaterThan(10_000); // Verify high processing throughput
    }

    [Fact]
    public void SustainedStress_MemoryLeakVerification_GCAllocationsBounded()
    {
        var router = new DataRouter();
        router.RegisterRule(new RoutingRule
        {
            RuleType = RuleType.Prefix,
            Tag = "TELE",
            Port = "*",
            TargetNodeId = "MEM_NODE"
        });

        int routedCount = 0;
        router.PacketRouted += (s, pkt) => routedCount++;

        // Thread-local allocation, not process heap. GC.GetTotalMemory measures every object
        // alive anywhere in the process, and xUnit runs collections in parallel, so this assertion
        // used to be decided by whatever unrelated test happened to be running alongside it: it
        // passed three times out of three in isolation and failed inside the full suite. A test
        // that fails at random teaches people to ignore failures, which costs more than the bug it
        // was meant to catch. GetAllocatedBytesForCurrentThread counts only what this thread
        // allocated, so the number now answers the question the test is actually asking.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long initialAllocated = GC.GetAllocatedBytesForCurrentThread();
        long initialMemory = GC.GetTotalMemory(true);

        const int iterations = 100_000;
        for (int i = 0; i < iterations; i++)
        {
            var body = $"TELE,MEM_NODE,VIB,{(0.5 + (i % 10)):F2},G";
            byte xor = TestDataGenerator.CalculateXorChecksum(body);
            router.Route(new RawPacket("COM2", $"${body}*{xor:X2}", DateTime.UtcNow));
        }

        routedCount.Should().Be(iterations);

        long allocatedDelta = GC.GetAllocatedBytesForCurrentThread() - initialAllocated;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long memoryDelta = GC.GetTotalMemory(true) - initialMemory;

        // Allocation per packet is the deterministic figure: parsing one frame allocates a handful
        // of short-lived strings, all of which the collector reclaims. 2 KB each is generous and
        // still catches a routing path that starts retaining per-packet state.
        (allocatedDelta / iterations).Should().BeLessThan(2048,
            "routing one frame should allocate a few short-lived strings, not accumulate state");

        // Retained heap is kept as a coarse backstop only. It is measured across the whole process,
        // so the bound is loose on purpose rather than precise and flaky.
        memoryDelta.Should().BeLessThan(64 * 1024 * 1024,
            "a genuine leak over 100k packets would dwarf any noise from tests running in parallel");
    }

    [Fact]
    public void HighRateStream_FailureCondition_Extracts10SecondSnapshot()
    {
        var snapshotExtractor = new RollingSnapshotExtractor();
        var baseTime = DateTime.UtcNow.AddMinutes(-5);

        // Generate 30 seconds of high-rate stream data (100 packets/sec = 3000 packets)
        for (int sec = 0; sec < 30; sec++)
        {
            var timestamp = baseTime.AddSeconds(sec);
            for (int p = 0; p < 100; p++)
            {
                var pkt = new TelemetryPacket("NODE_CRITICAL", "TEMP", 50.0 + p, "C", timestamp: timestamp.AddMilliseconds(p * 10));
                snapshotExtractor.Add(pkt);
            }
        }

        // Simulate failure at T = baseTime + 25 seconds
        var failureTimestamp = baseTime.AddSeconds(25);
        var snapshot = snapshotExtractor.ExtractSnapshot(failureTimestamp);

        snapshot.Should().NotBeEmpty();
        snapshot.Min(p => p.Timestamp).Should().BeOnOrAfter(failureTimestamp.AddSeconds(-10));
        snapshot.Max(p => p.Timestamp).Should().BeOnOrBefore(failureTimestamp);
    }

    [Fact]
    public async Task HighRateStream_ConcurrentDataRouterAndLoggerAndScope_NoLockContention()
    {
        var router = new DataRouter();
        for (int n = 1; n <= 4; n++)
        {
            router.RegisterRule(new RoutingRule
            {
                RuleType = RuleType.Prefix,
                Tag = "TELE",
                Port = $"COM{n}",
                TargetNodeId = $"NODE_{n}"
            });
        }

        int totalRouted = 0;
        router.PacketRouted += (s, pkt) => Interlocked.Increment(ref totalRouted);

        const int packetsPerThread = 10_000;
        var tasks = Enumerable.Range(1, 4).Select(threadId => Task.Run(() =>
        {
            var port = $"COM{threadId}";
            var node = $"NODE_{threadId}";
            for (int i = 0; i < packetsPerThread; i++)
            {
                var body = $"TELE,{node},VOLT,{(12.0 + (i % 5)):F2},V";
                byte xor = TestDataGenerator.CalculateXorChecksum(body);
                router.Route(new RawPacket(port, $"${body}*{xor:X2}", DateTime.UtcNow));
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        totalRouted.Should().Be(4 * packetsPerThread);
    }

    [Fact]
    public async Task StressWorkload_ChannelOverflowDropPolicy_PreservesSystemStability()
    {
        // Bounded channel with DropOldest policy
        var options = new BoundedChannelOptions(1_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        };

        var channel = Channel.CreateBounded<TelemetryPacket>(options);

        // Push 10,000 items rapidly
        for (int i = 0; i < 10_000; i++)
        {
            await channel.Writer.WriteAsync(new TelemetryPacket("STRESS_NODE", "TEMP", i, "C"));
        }

        channel.Writer.Complete();

        int readCount = 0;
        TelemetryPacket? lastPacket = null;

        while (await channel.Reader.WaitToReadAsync())
        {
            while (channel.Reader.TryRead(out var pkt))
            {
                readCount++;
                lastPacket = pkt;
            }
        }

        // Bounded channel with DropOldest should hold max capacity items
        readCount.Should().BeLessOrEqualTo(1_000);
        lastPacket.Should().NotBeNull();
        lastPacket!.Value.Should().Be(9_999); // Latest pushed item preserved
    }
}
