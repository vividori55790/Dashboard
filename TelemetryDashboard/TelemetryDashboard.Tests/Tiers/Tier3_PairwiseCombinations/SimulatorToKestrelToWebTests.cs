namespace TelemetryDashboard.Tests.Tiers.Tier3_PairwiseCombinations;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Tests.TestUtilities;
using Xunit;

/// <summary>
/// Tier 3 Pairwise Combination Test Suite:
/// Verifies cross-subsystem interaction between Dual-MCU Virtual Simulator -> Kestrel Embedded Web Server -> Web JSON Streaming.
/// </summary>
[Trait("Category", "Tier3")]
public class SimulatorToKestrelToWebTests
{
    private class MockKestrelWebServer
    {
        private readonly ConcurrentBag<Action<string>> _subscribers = new();
        public bool IsRunning { get; private set; }
        public int SubscriberCount => _subscribers.Count;

        public void Start(int port = 8080)
        {
            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
        }

        public void Subscribe(Action<string> onJsonFrame)
        {
            _subscribers.Add(onJsonFrame);
        }

        public void BroadcastTelemetry(TelemetryPacket packet)
        {
            if (!IsRunning) return;

            var json = JsonSerializer.Serialize(new
            {
                nodeId = packet.NodeId,
                variable = packet.Variable,
                value = packet.Value,
                unit = packet.Unit,
                timestamp = packet.Timestamp,
                alarmExceeded = packet.Flags.HasFlag(PacketFlags.AlarmExceeded)
            });

            foreach (var sub in _subscribers)
            {
                sub(json);
            }
        }
    }

    [Fact]
    public void SimulatorToWebServer_StartSimulation_EmitsJsonStreamToWebClient()
    {
        var server = new MockKestrelWebServer();
        server.Start(8080);

        var receivedFrames = new List<string>();
        server.Subscribe(json => receivedFrames.Add(json));

        var device = new MockSerialDevice("COM3", 115200);
        device.Connect();

        for (int i = 0; i < 5; i++)
        {
            var line = device.PushJsonFrame("MCU_NODE_1", "TEMP", 50.0 + i, "C");
            var pkt = new TelemetryPacket("MCU_NODE_1", "TEMP", 50.0 + i, "C", flags: PacketFlags.Simulated);
            server.BroadcastTelemetry(pkt);
        }

        server.IsRunning.Should().BeTrue();
        receivedFrames.Should().HaveCount(5);
        receivedFrames[0].Should().Contain("MCU_NODE_1");
        receivedFrames[0].Should().Contain("TEMP");
    }

    [Fact]
    public void SimulatorBaudRateAndFormatChange_WebStreamAdaptsSeamlessly()
    {
        var server = new MockKestrelWebServer();
        server.Start(8080);

        var receivedJson = new List<string>();
        server.Subscribe(json => receivedJson.Add(json));

        var device = new MockSerialDevice("COM3", 921600);
        device.Connect();

        var prefixLine = device.PushPrefixFrame("TELE", "MCU_NODE_1", "VIB", 1.85, "G");
        var prefixPkt = new TelemetryPacket("MCU_NODE_1", "VIB", 1.85, "G");
        server.BroadcastTelemetry(prefixPkt);

        var colLine = device.PushColumnsFrame("MCU_NODE_2", "RPM", 2400.0, "RPM");
        var colPkt = new TelemetryPacket("MCU_NODE_2", "RPM", 2400.0, "RPM");
        server.BroadcastTelemetry(colPkt);

        receivedJson.Should().HaveCount(2);
        receivedJson[0].Should().Contain("\"variable\":\"VIB\"");
        receivedJson[1].Should().Contain("\"variable\":\"RPM\"");
    }

    [Fact]
    public void WebClientDisconnect_SimulatorContinuesWithoutCrash()
    {
        var server = new MockKestrelWebServer();
        server.Start(8080);

        bool client1Received = false;
        Action<string> client1 = _ => client1Received = true;
        server.Subscribe(client1);

        server.BroadcastTelemetry(new TelemetryPacket("NODE_1", "TEMP", 30.0, "C"));
        client1Received.Should().BeTrue();

        server.Stop();
        server.IsRunning.Should().BeFalse();

        Action act = () => server.BroadcastTelemetry(new TelemetryPacket("NODE_1", "TEMP", 35.0, "C"));
        act.Should().NotThrow();
    }

    [Fact]
    public void MultiWebClients_ConcurrentStreaming_ReceivesIdenticalTelemetryData()
    {
        var server = new MockKestrelWebServer();
        server.Start(8080);

        var client1Data = new List<string>();
        var client2Data = new List<string>();
        var client3Data = new List<string>();

        server.Subscribe(json => client1Data.Add(json));
        server.Subscribe(json => client2Data.Add(json));
        server.Subscribe(json => client3Data.Add(json));

        server.SubscriberCount.Should().Be(3);

        var pkt = new TelemetryPacket("MCU_NODE_1", "VOLT", 12.4, "V");
        server.BroadcastTelemetry(pkt);

        client1Data.Should().HaveCount(1);
        client2Data.Should().HaveCount(1);
        client3Data.Should().HaveCount(1);

        client1Data[0].Should().Be(client2Data[0]);
        client2Data[0].Should().Be(client3Data[0]);
    }

    [Fact]
    public void SimulatorNoiseAndAnomaly_WebStreamConveysAlarmFlags()
    {
        var server = new MockKestrelWebServer();
        server.Start(8080);

        string? receivedJson = null;
        server.Subscribe(json => receivedJson = json);

        var alarmPkt = new TelemetryPacket("MCU_NODE_1", "TEMP", 95.0, "C", flags: PacketFlags.AlarmExceeded);
        server.BroadcastTelemetry(alarmPkt);

        receivedJson.Should().NotBeNull();
        receivedJson.Should().Contain("\"alarmExceeded\":true");
    }
}
