namespace TelemetryDashboard.Tests.Tiers.Tier3_PairwiseCombinations;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Tests.TestUtilities;
using Xunit;

/// <summary>
/// Tier 3 Pairwise Combination Test Suite:
/// Verifies cross-subsystem interaction between Serial Stream -> Hybrid Data Logger -> ScottPlot Scope ViewModel.
/// </summary>
[Trait("Category", "Tier3")]
public class SerialToStorageToScopeTests
{
    private class InMemoryDataLogger : IDataLogger
    {
        private readonly List<TelemetryPacket> _loggedPackets = new();
        private readonly object _lock = new();

        public IReadOnlyList<TelemetryPacket> LoggedPackets
        {
            get
            {
                lock (_lock) return _loggedPackets.ToList();
            }
        }

        public Task WriteAsync(TelemetryPacket packet, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _loggedPackets.Add(packet);
            }
            return Task.CompletedTask;
        }

        public Task WriteBatchAsync(IEnumerable<TelemetryPacket> packets, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _loggedPackets.AddRange(packets);
            }
            return Task.CompletedTask;
        }

        public Task<IEnumerable<TelemetryPacket>> QueryAsync(QueryFilter filter, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                IEnumerable<TelemetryPacket> query = _loggedPackets;
                if (!string.IsNullOrEmpty(filter.NodeId))
                    query = query.Where(p => string.Equals(p.NodeId, filter.NodeId, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(filter.Variable))
                    query = query.Where(p => string.Equals(p.Variable, filter.Variable, StringComparison.OrdinalIgnoreCase));
                if (filter.StartTime.HasValue)
                    query = query.Where(p => p.Timestamp >= filter.StartTime.Value);
                if (filter.EndTime.HasValue)
                    query = query.Where(p => p.Timestamp <= filter.EndTime.Value);

                return Task.FromResult<IEnumerable<TelemetryPacket>>(query.Take(filter.Limit).ToList());
            }
        }
    }

    private class MockScopeViewModel
    {
        public List<TelemetryPacket> DisplayedPoints { get; } = new();
        public double MaxValue => DisplayedPoints.Count > 0 ? DisplayedPoints.Max(p => p.Value) : 0;
        public double MinValue => DisplayedPoints.Count > 0 ? DisplayedPoints.Min(p => p.Value) : 0;

        public void OnTelemetryReceived(TelemetryPacket packet)
        {
            DisplayedPoints.Add(packet);
        }
    }

    [Fact]
    public async Task SerialToLogger_ValidPrefixPackets_SuccessfullyRoutedAndLogged()
    {
        var device = new MockSerialDevice("COM3", 115200);
        device.Connect();

        var router = new DataRouter();
        router.RegisterRule(new RoutingRule
        {
            RuleType = RuleType.Prefix,
            Tag = "TELE",
            Port = "COM3",
            TargetNodeId = "MCU_NODE_1"
        });

        var logger = new InMemoryDataLogger();

        router.PacketRouted += async (s, pkt) =>
        {
            await logger.WriteAsync(pkt);
        };

        var line = device.PushPrefixFrame("TELE", "MCU_NODE_1", "TEMP", 67.5, "C");
        var rawPacket = new RawPacket("COM3", line, DateTime.UtcNow);

        var routed = router.Route(rawPacket).ToList();

        routed.Should().HaveCount(1);
        logger.LoggedPackets.Should().HaveCount(1);

        var logged = logger.LoggedPackets[0];
        logged.NodeId.Should().Be("MCU_NODE_1");
        logged.Variable.Should().Be("TEMP");
        logged.Value.Should().Be(67.5);
        logged.Unit.Should().Be("C");
    }

    [Fact]
    public async Task LoggerToScopeViewModel_DataIngestion_UpdatesScopeSeriesData()
    {
        var logger = new InMemoryDataLogger();
        var scopeVm = new MockScopeViewModel();

        var packet1 = new TelemetryPacket("MCU_NODE_1", "TEMP", 45.0, "C");
        var packet2 = new TelemetryPacket("MCU_NODE_1", "TEMP", 82.3, "C");
        var packet3 = new TelemetryPacket("MCU_NODE_1", "TEMP", 21.1, "C");

        await logger.WriteBatchAsync(new[] { packet1, packet2, packet3 });

        foreach (var pkt in logger.LoggedPackets)
        {
            scopeVm.OnTelemetryReceived(pkt);
        }

        scopeVm.DisplayedPoints.Should().HaveCount(3);
        scopeVm.MaxValue.Should().Be(82.3);
        scopeVm.MinValue.Should().Be(21.1);
    }

    [Fact]
    public async Task EndToEnd_SerialStream_ToLogger_ToScope_PairwiseDataPipeline()
    {
        var device = new MockSerialDevice("COM4", 115200);
        device.Connect();

        var router = new DataRouter();
        router.RegisterNode(new SensorNode("MCU_NODE_2", "Engine Node", "COM4", "PROPULSION"));
        router.RegisterRule(new RoutingRule
        {
            RuleType = RuleType.Prefix,
            Tag = "TELE",
            Port = "COM4",
            TargetNodeId = "MCU_NODE_2"
        });

        var logger = new InMemoryDataLogger();
        var scopeVm = new MockScopeViewModel();

        router.PacketRouted += async (s, pkt) =>
        {
            await logger.WriteAsync(pkt);
            scopeVm.OnTelemetryReceived(pkt);
        };

        // Push 10 packets across 2 variables
        for (int i = 1; i <= 10; i++)
        {
            var varName = i % 2 == 0 ? "TEMP" : "RPM";
            var val = i % 2 == 0 ? 50.0 + i : 1000.0 + (i * 100);
            var unit = i % 2 == 0 ? "C" : "RPM";

            var line = device.PushPrefixFrame("TELE", "MCU_NODE_2", varName, val, unit);
            var rawPacket = new RawPacket("COM4", line, DateTime.UtcNow);
            router.Route(rawPacket);
        }

        logger.LoggedPackets.Should().HaveCount(10);
        scopeVm.DisplayedPoints.Should().HaveCount(10);

        var queryTemp = await logger.QueryAsync(new QueryFilter(NodeId: "MCU_NODE_2", Variable: "TEMP"));
        queryTemp.Should().HaveCount(5);
    }

    [Fact]
    public async Task SerialBackpressureToLogger_HighFrequencyStream_MaintainsStorageAndScopeFidelity()
    {
        var device = new MockSerialDevice("COM5", 921600);
        device.Connect();

        var router = new DataRouter();
        router.RegisterRule(new RoutingRule
        {
            RuleType = RuleType.Prefix,
            Tag = "TELE",
            Port = "COM5",
            TargetNodeId = "FAST_NODE"
        });

        var logger = new InMemoryDataLogger();
        var scopeVm = new MockScopeViewModel();

        router.PacketRouted += async (s, pkt) =>
        {
            await logger.WriteAsync(pkt);
            scopeVm.OnTelemetryReceived(pkt);
        };

        const int totalPackets = 1000;
        for (int i = 0; i < totalPackets; i++)
        {
            var line = device.PushPrefixFrame("TELE", "FAST_NODE", "VIB", 0.1 * i, "G");
            var rawPacket = new RawPacket("COM5", line, DateTime.UtcNow);
            router.Route(rawPacket);
        }

        logger.LoggedPackets.Should().HaveCount(totalPackets);
        scopeVm.DisplayedPoints.Should().HaveCount(totalPackets);
        scopeVm.MaxValue.Should().Be(0.1 * (totalPackets - 1));
    }

    [Fact]
    public async Task SerialDisconnectToLogger_GracefulFlushOnDisconnect()
    {
        var device = new MockSerialDevice("COM6", 115200);
        device.Connect();

        var router = new DataRouter();
        router.RegisterRule(new RoutingRule
        {
            RuleType = RuleType.Prefix,
            Tag = "TELE",
            Port = "*",
            TargetNodeId = "DISCONNECT_NODE"
        });

        var logger = new InMemoryDataLogger();
        var scopeVm = new MockScopeViewModel();

        router.PacketRouted += async (s, pkt) =>
        {
            await logger.WriteAsync(pkt);
            scopeVm.OnTelemetryReceived(pkt);
        };

        // Push 5 packets while connected
        for (int i = 0; i < 5; i++)
        {
            var line = device.PushPrefixFrame("TELE", "DISCONNECT_NODE", "VOLT", 12.0 + i, "V");
            router.Route(new RawPacket("COM6", line, DateTime.UtcNow));
        }

        device.SimulateDeviceUnplug();
        device.IsOpen.Should().BeFalse();

        // Ensure previously routed data remains intact in logger and scope
        logger.LoggedPackets.Should().HaveCount(5);
        scopeVm.DisplayedPoints.Should().HaveCount(5);
    }
}
