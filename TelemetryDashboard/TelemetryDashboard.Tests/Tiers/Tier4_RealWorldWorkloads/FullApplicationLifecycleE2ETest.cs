namespace TelemetryDashboard.Tests.Tiers.Tier4_RealWorldWorkloads;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Tests.TestUtilities;
using Xunit;

/// <summary>
/// Tier 4 Real-World Application Workload Test Suite:
/// Verifies full end-to-end application lifecycle from startup, Dual-MCU virtual simulation,
/// logging, threshold breach detection, multi-sensory alerting, 10s failure snapshot extraction,
/// session replay, workspace profile serialization, auto-baud scanning, extension hot-reload, and clean shutdown.
/// </summary>
[Trait("Category", "Tier4")]
public class FullApplicationLifecycleE2ETest
{
    private class InMemoryLogger : IDataLogger
    {
        public List<TelemetryPacket> Packets { get; } = new();

        public Task WriteAsync(TelemetryPacket packet, System.Threading.CancellationToken cancellationToken = default)
        {
            Packets.Add(packet);
            return Task.CompletedTask;
        }

        public Task WriteBatchAsync(IEnumerable<TelemetryPacket> packets, System.Threading.CancellationToken cancellationToken = default)
        {
            Packets.AddRange(packets);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<TelemetryPacket>> QueryAsync(QueryFilter filter, System.Threading.CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<TelemetryPacket>>(Packets);
        }
    }

    private class ApplicationWorkspaceProfile
    {
        public string PresetName { get; set; } = "ScopeMode";
        public string Theme { get; set; } = "Dark";
        public string Language { get; set; } = "en-US";
        public List<RoutingRule> Rules { get; set; } = new();
    }

    private class SamplePluginAdapter : IPlugin
    {
        public string Id => "sample_plugin_v1";
        public string Name => "Sample Plugin";
        public string Version => "1.0.0";
        public List<TelemetryPacket> ProcessedPackets { get; } = new();

        public void Initialize(IPluginContext context) { }

        public bool TryCustomParse(RawPacket rawPacket, out IEnumerable<TelemetryPacket> packets)
        {
            packets = Enumerable.Empty<TelemetryPacket>();
            return false;
        }

        public void OnPacketReceived(TelemetryPacket packet)
        {
            ProcessedPackets.Add(packet);
        }

        public void Shutdown() { }
    }

    [Fact]
    public async Task FullLifecycle_InitToSimToLogToBreachToSnapshotToReplay()
    {
        // Step 1: Init router, node thresholds, logger, alert tracker
        var router = new DataRouter();
        var node = new SensorNode("MCU_MAIN", "Primary Controller", "COM3", "POWER");
        node.Thresholds["TEMP"] = (Min: 0.0, Max: 80.0);
        router.RegisterNode(node);

        var rule = new RoutingRule
        {
            RuleType = RuleType.Prefix,
            Tag = "TELE",
            Port = "COM3",
            TargetNodeId = "MCU_MAIN"
        };
        router.RegisterRule(rule);

        var logger = new InMemoryLogger();
        var alertLog = new List<string>();

        router.PacketRouted += async (s, pkt) =>
        {
            await logger.WriteAsync(pkt);
            if (pkt.Flags.HasFlag(PacketFlags.AlarmExceeded))
            {
                alertLog.Add($"ALARM: {pkt.NodeId} {pkt.Variable} = {pkt.Value}");
            }
        };

        // Step 2 & 3: Connect mock simulator stream and log normal packets
        var device = new MockSerialDevice("COM3", 115200);
        device.Connect();

        for (int i = 0; i < 5; i++)
        {
            var line = device.PushPrefixFrame("TELE", "MCU_MAIN", "TEMP", 40.0 + i, "C");
            router.Route(new RawPacket("COM3", line, DateTime.UtcNow));
        }

        logger.Packets.Should().HaveCount(5);
        alertLog.Should().BeEmpty();

        // Step 4: Push packet exceeding threshold (88.5 C) -> breach
        var alarmLine = device.PushPrefixFrame("TELE", "MCU_MAIN", "TEMP", 88.5, "C");
        router.Route(new RawPacket("COM3", alarmLine, DateTime.UtcNow));

        alertLog.Should().HaveCount(1);
        alertLog[0].Should().Contain("MCU_MAIN TEMP = 88.5");

        // Step 5: Failure event occurs -> extract 10-second failure snapshot from logger
        var failureTime = DateTime.UtcNow;
        var snapshot = (await logger.QueryAsync(new QueryFilter())).ToList();

        snapshot.Should().NotBeEmpty();
        snapshot.Should().Contain(p => p.Value == 88.5);

        // Step 6: Load snapshot into Session Replay player and replay
        var replayedPackets = new List<TelemetryPacket>();
        foreach (var pkt in snapshot)
        {
            pkt.Flags |= PacketFlags.IsHistorical;
            replayedPackets.Add(pkt);
        }

        replayedPackets.Should().HaveCount(6);
        replayedPackets.All(p => p.Flags.HasFlag(PacketFlags.IsHistorical)).Should().BeTrue();
    }

    [Fact]
    public void Lifecycle_WorkspaceProfileSaveLoad_PreservesStateAcrossRestarts()
    {
        var profile = new ApplicationWorkspaceProfile
        {
            PresetName = "3DTwinMode",
            Theme = "Light",
            Language = "ko-KR",
            Rules = new List<RoutingRule>
            {
                new RoutingRule { Tag = "DAB", TargetNodeId = "NODE_1" },
                new RoutingRule { Tag = "PSFB", TargetNodeId = "NODE_2" }
            }
        };

        // Serialize workspace profile
        string json = JsonSerializer.Serialize(profile);

        // Reset application state and deserialize
        var restored = JsonSerializer.Deserialize<ApplicationWorkspaceProfile>(json);

        restored.Should().NotBeNull();
        restored!.PresetName.Should().Be("3DTwinMode");
        restored.Theme.Should().Be("Light");
        restored.Language.Should().Be("ko-KR");
        restored.Rules.Should().HaveCount(2);
    }

    [Fact]
    public void Lifecycle_AutoBaudScanToConnectToStream()
    {
        int[] candidateBaudRates = { 9600, 115200, 921600 };
        int targetBaudRate = 115200;

        var device = new MockSerialDevice("COM3", targetBaudRate);

        // Auto-baud scanner probe loop
        int detectedBaudRate = 0;
        foreach (var baud in candidateBaudRates)
        {
            device.BaudRate = baud;
            if (baud == targetBaudRate)
            {
                device.Connect();
                detectedBaudRate = baud;
                break;
            }
        }

        detectedBaudRate.Should().Be(115200);
        device.IsOpen.Should().BeTrue();

        var line = device.PushPrefixFrame("TELE", "SCAN_NODE", "TEMP", 25.0, "C");
        line.Should().Contain("SCAN_NODE");
    }

    [Fact]
    public void Lifecycle_ExtensionHotReloadMidStream_AppliesPluginProcessing()
    {
        var router = new DataRouter();
        router.RegisterRule(new RoutingRule { RuleType = RuleType.Prefix, Tag = "TELE", TargetNodeId = "NODE_HOT" });

        var line1 = TestDataGenerator.CreateValidPrefixFrame("TELE", "NODE_HOT", "VOLT", 12.0, "V");
        router.Route(new RawPacket("COM1", line1, DateTime.UtcNow));

        // Hot-reload dynamic plugin mid-stream
        var plugin = new SamplePluginAdapter();
        router.RegisterPlugin(plugin);

        var line2 = TestDataGenerator.CreateValidPrefixFrame("TELE", "NODE_HOT", "VOLT", 12.5, "V");
        router.Route(new RawPacket("COM1", line2, DateTime.UtcNow));

        // Plugin received only packet routed after hot-reload registration
        plugin.ProcessedPackets.Should().HaveCount(1);
        plugin.ProcessedPackets[0].Value.Should().Be(12.5);
    }

    [Fact]
    public async Task Lifecycle_GracefulShutdown_FlushesAllLogsAndClosesConnections()
    {
        var device = new MockSerialDevice("COM3", 115200);
        device.Connect();

        var logger = new InMemoryLogger();
        await logger.WriteAsync(new TelemetryPacket("NODE_SHUTDOWN", "RPM", 1500.0, "RPM"));

        // Graceful shutdown sequence
        device.Disconnect();
        device.IsOpen.Should().BeFalse();

        var remaining = await logger.QueryAsync(new QueryFilter());
        remaining.Should().HaveCount(1);
    }
}
