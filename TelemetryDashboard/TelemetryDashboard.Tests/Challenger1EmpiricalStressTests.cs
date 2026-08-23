namespace TelemetryDashboard.Tests;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Events;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Parsers;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Infrastructure.Serial;
using Xunit;
using TelemetryDashboard.Core.Plugins;

[Collection(HeavyTestCollection.Name)]
public class Challenger1EmpiricalStressTests
{
    #region 1. Empirical Stress Tests: Packet Parsing

    [Fact]
    public void StressTest_PrefixParser_100kPackets_HighSpeedAndEdgeCases()
    {
        var rule = new RoutingRule
        {
            RuleType = RuleType.Prefix,
            Tag = "$TELE",
            TargetNodeId = "NODE_PREFIX",
            IndexMap = new Dictionary<int, string> { { 0, "Volt" }, { 1, "Curr" } },
            Calibrations = new Dictionary<string, (double Gain, double Offset)>
            {
                { "Volt", (2.0, 0.5) }
            }
        };

        int count = 100_000;
        int parsedCount = 0;

        Parallel.For(0, count, i =>
        {
            double rawV = i * 0.1;
            double rawI = i * 0.01;
            string payload = $"TELE,{rawV.ToString(System.Globalization.CultureInfo.InvariantCulture)},{rawI.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            byte cs = XorChecksum.Calculate(payload.AsSpan());
            string rawStr = $"${payload}*{cs:X2}\r\n";

            var rawPkt = new RawPacket("COM1", rawStr);
            if (PrefixParser.TryParse(rawPkt, rule, out var packets))
            {
                Interlocked.Increment(ref parsedCount);
                packets.Should().HaveCount(2);
                packets[0].Value.Should().BeApproximately(rawV * 2.0 + 0.5, 1e-4);
                packets[1].Value.Should().BeApproximately(rawI, 1e-4);
            }
        });

        parsedCount.Should().Be(count, "All 100,000 valid PREFIX packets must be correctly parsed under multi-threaded stress");
    }

    [Fact]
    public void StressTest_PrefixParser_EdgeCases_InvalidChecksumAndTagCollision()
    {
        var ruleTag1 = new RoutingRule
        {
            RuleType = RuleType.Prefix,
            Tag = "$TAG1",
            TargetNodeId = "NODE_1",
            IndexMap = new Dictionary<int, string> { { 0, "Val" } }
        };

        // 1. Tag Collision Check: TAG10 should not match rule for TAG1
        string payloadTag10 = "TAG10,123.45";
        byte cs10 = XorChecksum.Calculate(payloadTag10.AsSpan());
        var pktTag10 = new RawPacket("COM1", $"${payloadTag10}*{cs10:X2}\r\n");
        PrefixParser.TryParse(pktTag10, ruleTag1, out _).Should().BeFalse("Rule for $TAG1 must not match $TAG10 packet");

        // 2. Invalid Checksum
        string payloadValid = "TAG1,123.45";
        var pktInvalidCs = new RawPacket("COM1", $"${payloadValid}*FF\r\n");
        PrefixParser.TryParse(pktInvalidCs, ruleTag1, out _).Should().BeFalse("Packet with bad checksum FF must be rejected");

        // 3. Historical Resync Format $HIST,Node,Var,Val,Ts
        var histRule = new RoutingRule { TargetNodeId = "FALLBACK" };
        var histPkt = new RawPacket("COM1", "$HIST,MCU_01,Temp,85.5,1700000000\r\n");
        PrefixParser.TryParse(histPkt, histRule, out var histPackets).Should().BeTrue();
        histPackets.Should().HaveCount(1);
        histPackets[0].NodeId.Should().Be("MCU_01");
        histPackets[0].Variable.Should().Be("Temp");
        histPackets[0].Value.Should().Be(85.5);
        histPackets[0].Flags.HasFlag(PacketFlags.IsHistorical).Should().BeTrue();
    }

    [Fact]
    public void StressTest_JsonParser_HighVolumeAndComplexStringTags()
    {
        var rulePsfb = new RoutingRule
        {
            RuleType = RuleType.Json,
            Tag = "device:PSFB",
            TargetNodeId = "NODE_PSFB",
            JsonMap = new Dictionary<string, string> { { "temp", "Temperature" }, { "vout", "Voltage" } },
            Calibrations = new Dictionary<string, (double Gain, double Offset)>
            {
                { "Temperature", (1.0, -2.0) }
            }
        };

        int count = 10_000;
        int parsedCount = 0;

        Parallel.For(0, count, i =>
        {
            bool isMatching = i % 2 == 0;
            string dev = isMatching ? "PSFB" : "LLC";
            string json = $"{{\"device\": \"{dev}\", \"temp\": {50.0 + i * 0.01}, \"vout\": 12.0, \"active\": true}}";
            var rawPkt = new RawPacket("COM2", json);

            if (JsonParser.TryParse(rawPkt, rulePsfb, out var packets))
            {
                Interlocked.Increment(ref parsedCount);
                packets.Should().HaveCount(2);
                packets.First(p => p.Variable == "Temperature").Value.Should().BeApproximately(50.0 + i * 0.01 - 2.0, 1e-4);
            }
        });

        parsedCount.Should().Be(count / 2, "Only even packets with device:PSFB should be parsed");
    }

    [Fact]
    public void StressTest_ColumnsParser_RawCsvArraysAndChecksumSuffix()
    {
        var rule = new RoutingRule
        {
            RuleType = RuleType.Columns,
            TargetNodeId = "NODE_COL",
            IndexMap = new Dictionary<int, string> { { 0, "Speed" }, { 1, "Pressure" } }
        };

        // Standard CSV without dollar sign
        string csvNormal = "3000, 101.3\r\n";
        ColumnsParser.TryParse(new RawPacket("COM1", csvNormal), rule, out var pktsNormal).Should().BeTrue();
        pktsNormal.Should().HaveCount(2);

        // CSV with checksum suffix *A1
        string csvChecksum = "3000,101.3*A1\r\n";
        ColumnsParser.TryParse(new RawPacket("COM1", csvChecksum), rule, out var pktsChecksum).Should().BeTrue();
        pktsChecksum.Should().HaveCount(2);

        // CSV starting with $ must be rejected by ColumnsParser
        string csvDollar = "$3000,101.3";
        ColumnsParser.TryParse(new RawPacket("COM1", csvDollar), rule, out _).Should().BeFalse("ColumnsParser must reject payloads starting with '$'");
    }

    #endregion

    #region 2. Empirical Stress Tests: Formula Evaluation & AST Scoping

    [Fact]
    public void StressTest_FormulaEvaluator_MultiNodeScopingAndCrossNodeReferences()
    {
        var evaluator = new FormulaEvaluator();

        // 1. AST cache scoping test: Evaluate "temp * 2" across 50 nodes concurrently
        Parallel.For(0, 50, n =>
        {
            string nodeId = $"NODE_{n}";
            double expectedVal = n * 10.0;

            double result = evaluator.Evaluate("temp * 2", nodeId, (nId, vName) =>
            {
                if (vName == "temp")
                {
                    int id = int.Parse(nId.Replace("NODE_", ""));
                    return id * 5.0;
                }
                return 0.0;
            });

            result.Should().Be(expectedVal, $"Node {nodeId} evaluation must yield node-specific variable value");
        });

        // 2. Cross-Node Formula Evaluation
        string crossFormula = "[NODE_A].voltage - [NODE_B].voltage";
        double diff = evaluator.Evaluate(crossFormula, "LOCAL_NODE", (nId, vName) =>
        {
            if (nId == "NODE_A" && vName == "voltage") return 48.0;
            if (nId == "NODE_B" && vName == "voltage") return 12.0;
            return 0.0;
        });

        diff.Should().Be(36.0, "Cross-node reference [NODE_A].voltage - [NODE_B].voltage should resolve correctly");

        // 3. Unary minus and complex math expression
        string complexFormula = "abs(-10) + sqrt(16) + min(5, 20) * max(2, 3) - 2 ^ 3";
        // 10 + 4 + 5 * 3 - 8 = 10 + 4 + 15 - 8 = 21
        double complexResult = evaluator.Evaluate(complexFormula, "NODE_1", (_, __) => 0.0);
        complexResult.Should().Be(21.0, "Complex math expression evaluation must yield 21.0");
    }

    #endregion

    #region 3. Empirical Stress Tests: Multi-Thread Concurrency Safety

    [Fact]
    public void StressTest_SensorNode_LatestValues_HighConcurrencyReadWrite()
    {
        var node = new SensorNode("NODE_STRESS", "Stress Node", "COM1", "Power");
        node.Thresholds["temp"] = (0.0, 100.0);

        int totalOperations = 100_000;
        int alarmCount = 0;

        Parallel.For(0, totalOperations, i =>
        {
            string varName = i % 2 == 0 ? "temp" : $"var_{i % 50}";
            double val = i % 2 == 0 ? (i % 200 > 100 ? 150.0 : 50.0) : i * 0.1;

            bool isAlarm = node.UpdateVariable(varName, val);
            if (isAlarm)
            {
                Interlocked.Increment(ref alarmCount);
            }

            // Concurrent read
            if (node.LatestValues.TryGetValue(varName, out double readVal))
            {
                readVal.Should().BeGreaterThanOrEqualTo(0.0);
            }
        });

        node.LatestValues.Should().ContainKey("temp");
        alarmCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void StressTest_DataRouter_ConcurrentRoutingAndRuleModification()
    {
        var router = new DataRouter();
        var node1 = new SensorNode("NODE_1", "Node 1", "COM1", "Sub1");
        var node2 = new SensorNode("NODE_2", "Node 2", "COM2", "Sub2");
        router.RegisterNode(node1);
        router.RegisterNode(node2);

        // Register initial rules
        var rule1 = new RoutingRule
        {
            Id = "RULE_1",
            RuleType = RuleType.Prefix,
            Tag = "$PWR",
            TargetNodeId = "NODE_1",
            IndexMap = new Dictionary<int, string> { { 0, "vout" }, { 1, "iout" } },
            Formulas = new List<string> { "pout = vout * iout" }
        };

        var rule2 = new RoutingRule
        {
            Id = "RULE_2",
            RuleType = RuleType.Json,
            Tag = "device:AUX",
            TargetNodeId = "NODE_2",
            JsonMap = new Dictionary<string, string> { { "temp", "Temperature" } }
        };

        router.RegisterRule(rule1);
        router.RegisterRule(rule2);

        int totalRouted = 0;

        // Run 20,000 concurrent iterations mixing packet routing, rule adding, and rule unregistering
        Parallel.For(0, 20_000, i =>
        {
            if (i % 1000 == 0)
            {
                string tempRuleId = $"DYN_RULE_{i}";
                router.RegisterRule(new RoutingRule
                {
                    Id = tempRuleId,
                    RuleType = RuleType.Columns,
                    TargetNodeId = "NODE_1",
                    IndexMap = new Dictionary<int, string> { { 0, "dyn_val" } }
                });
                router.UnregisterRule(tempRuleId).Should().BeTrue("Dynamic rule should be cleanly unregistered");
            }

            if (i % 2 == 0)
            {
                string payload = $"PWR,{12.0 + (i % 5)},{2.0 + (i % 3)}";
                byte cs = XorChecksum.Calculate(payload.AsSpan());
                var raw = new RawPacket("COM1", $"${payload}*{cs:X2}\r\n");

                var routed = router.Route(raw).ToList();
                Interlocked.Add(ref totalRouted, routed.Count);
            }
            else
            {
                string json = $"{{\"device\": \"AUX\", \"temp\": {25.0 + (i % 10)}}}";
                var raw = new RawPacket("COM2", json);

                var routed = router.Route(raw).ToList();
                Interlocked.Add(ref totalRouted, routed.Count);
            }
        });

        totalRouted.Should().BeGreaterThan(0, "Concurrent routing under rule modifications must produce routed packets");
    }

    #endregion

    #region 4. Empirical Stress Tests: Serial Manager Auto-Reconnect Logic

    private class TestSerialManagerMock : ISerialManager
    {
        private readonly ConcurrentDictionary<string, PortConnectionStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);
        public System.Threading.Channels.ChannelReader<RawPacket> PacketReader => throw new NotImplementedException();
        public IReadOnlyDictionary<string, PortConnectionStatus> ActivePorts => _statuses;
        public ConcurrentBag<string> WrittenCommands { get; } = new();
        public ConcurrentBag<string> ConnectAttempts { get; } = new();
        public bool SimulateConnectSuccess { get; set; } = true;

        public event EventHandler<DeviceChangeEventArgs>? DeviceChanged;

#pragma warning disable CS0067
        public event EventHandler<TelemetryDashboard.Core.Events.SerialPortFaultEventArgs>? PortFaulted;
        public event EventHandler<string>? PortRecovered;
#pragma warning restore CS0067

        public void SetPortStatus(string portName, PortConnectionStatus status) => _statuses[portName] = status;

        public void FireDeviceChanged(DeviceChangeEventArgs args) => DeviceChanged?.Invoke(this, args);

        public Task<bool> ConnectPortAsync(string portName, int baudRate = 115200, CancellationToken cancellationToken = default)
        {
            ConnectAttempts.Add(portName);
            if (SimulateConnectSuccess)
            {
                _statuses[portName] = PortConnectionStatus.Connected;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> ConnectAsync(string portName, int baudRate) => ConnectPortAsync(portName, baudRate);

        public Task DisconnectPortAsync(string portName)
        {
            _statuses[portName] = PortConnectionStatus.Disconnected;
            return Task.CompletedTask;
        }

        public Task DisconnectAllAsync()
        {
            _statuses.Clear();
            return Task.CompletedTask;
        }

        public Task WriteLineAsync(string portName, string data, CancellationToken cancellationToken = default)
        {
            WrittenCommands.Add($"{portName}:{data.Trim()}");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    [Fact]
    public async Task StressTest_AutoReconnectEngine_ReconnectAndResyncTimeline()
    {
        var serialMock = new TestSerialManagerMock();
        serialMock.SetPortStatus("COM3", PortConnectionStatus.Disconnected);

        using var engine = new AutoReconnectEngine(serialMock, TimeSpan.FromMilliseconds(50));
        DateTime initialTime = new DateTime(2026, 8, 9, 15, 0, 0, 123, DateTimeKind.Utc);
        engine.RegisterTargetPort("COM3", 115200, initialTime);

        // Fire USB Device Arrival event for COM3
        serialMock.FireDeviceChanged(new DeviceChangeEventArgs(DeviceChangeType.Arrival, "COM3"));

        // Wait for auto-reconnect execution and 100ms command delay
        await Task.Delay(250);

        serialMock.ConnectAttempts.Should().Contain("COM3");
        serialMock.ActivePorts["COM3"].Should().Be(PortConnectionStatus.Connected);

        // Verify resync command sending with millisecond ISO 8601 timestamp
        serialMock.WrittenCommands.Should().ContainSingle();
        serialMock.WrittenCommands.First().Should().Be("COM3:$CMD,REQ_RESYNC,2026-08-09T15:00:00.123Z");
    }

    #endregion
}
