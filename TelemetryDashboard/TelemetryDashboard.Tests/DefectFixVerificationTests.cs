namespace TelemetryDashboard.Tests;

using Xunit;
using FluentAssertions;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Parsers;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Plugins;

public class DefectFixVerificationTests
{
    [Fact]
    public void Fix1_JsonParser_PreservesNonNumericStringTags_AndDiscriminatorCheckWorks()
    {
        // 1. Rule with string tag discriminator "device:PSFB"
        var rule = new RoutingRule
        {
            Id = "RULE_JSON_1",
            RuleType = RuleType.Json,
            Tag = "device:PSFB",
            TargetNodeId = "NODE_PSFB",
            JsonMap = new Dictionary<string, string> { { "temp", "Temperature" }, { "vout", "Voltage" } }
        };

        // Matching packet
        string matchingJson = "{\"device\": \"PSFB\", \"temp\": 48.5, \"vout\": 12.1}";
        var rawMatching = new RawPacket("COM1", matchingJson);

        bool success = JsonParser.TryParse(rawMatching, rule, out var packets);
        success.Should().BeTrue("JsonParser should preserve string tag field 'device' and match 'device:PSFB'");
        packets.Should().HaveCount(2);
        packets.First(p => p.Variable == "Temperature").Value.Should().Be(48.5);

        // Non-matching packet
        string nonMatchingJson = "{\"device\": \"BOOST\", \"temp\": 48.5, \"vout\": 12.1}";
        var rawNonMatching = new RawPacket("COM1", nonMatchingJson);

        bool failSuccess = JsonParser.TryParse(rawNonMatching, rule, out var failPackets);
        failSuccess.Should().BeFalse("JsonParser should reject JSON where 'device' != 'PSFB'");
        failPackets.Should().BeEmpty();
    }

    [Fact]
    public void Fix2_FormulaEvaluator_AstCacheKey_IncludesNodeId_NoVariableScopeLeak()
    {
        var evaluator = new FormulaEvaluator();
        string expression = "vout * iout";

        // Node1 resolver: vout = 12.0, iout = 2.0 -> 24.0
        double node1Result = evaluator.Evaluate(expression, "NODE_1", (nId, varName) =>
        {
            if (nId == "NODE_1" && varName == "vout") return 12.0;
            if (nId == "NODE_1" && varName == "iout") return 2.0;
            return 0.0;
        });

        node1Result.Should().Be(24.0);

        // Node2 resolver: vout = 5.0, iout = 3.0 -> 15.0
        double node2Result = evaluator.Evaluate(expression, "NODE_2", (nId, varName) =>
        {
            if (nId == "NODE_2" && varName == "vout") return 5.0;
            if (nId == "NODE_2" && varName == "iout") return 3.0;
            return 0.0;
        });

        // NODE_2 result must be 15.0, not NODE_1's cached 24.0
        node2Result.Should().Be(15.0, "AST cache must isolate node scope by incorporating currentNodeId into cache key");
    }

    [Fact]
    public void Fix3_SensorNode_And_DataRouter_ConcurrentLatestValues_ThreadSafe()
    {
        var router = new DataRouter();
        var node = new SensorNode("NODE_CONCURRENT", "Concurrent Node", "COM1", "Power");
        router.RegisterNode(node);

        var rule = new RoutingRule
        {
            Id = "RULE_CONCURRENT",
            RuleType = RuleType.Prefix,
            Tag = "$PWR",
            TargetNodeId = "NODE_CONCURRENT",
            IndexMap = new Dictionary<int, string> { { 0, "vout" }, { 1, "iout" } },
            Formulas = new List<string> { "pout = vout * iout" }
        };
        router.RegisterRule(rule);

        // Run concurrent updates and routing operations
        Parallel.For(0, 1000, i =>
        {
            node.UpdateVariable($"var_{i % 20}", i * 1.5);

            string payload = $"PWR,{10.0 + (i % 5)},{2.0 + (i % 3)}";
            byte cs = XorChecksum.Calculate(payload.AsSpan());
            var raw = new RawPacket("COM1", $"${payload}*{cs:X2}\r\n");

            var packets = router.Route(raw);
            packets.Should().NotBeNull();
        });

        node.LatestValues.Should().ContainKey("vout");
        node.LatestValues.Should().ContainKey("iout");
    }

    [Fact]
    public void Fix4_DataRouter_UnregisterRule_RemovesRuleMatchingRuleId()
    {
        var router = new DataRouter();
        var rule = new RoutingRule
        {
            Id = "RULE_TO_REMOVE",
            RuleType = RuleType.Columns,
            TargetNodeId = "NODE_1",
            IndexMap = new Dictionary<int, string> { { 0, "RPM" } }
        };

        router.RegisterRule(rule);

        // Verify route works before unregistering
        var rawBefore = new RawPacket("COM1", "1500\r\n");
        var packetsBefore = router.Route(rawBefore);
        packetsBefore.Should().HaveCount(1);

        // Unregister rule
        bool unregistered = router.UnregisterRule("RULE_TO_REMOVE");
        unregistered.Should().BeTrue("UnregisterRule must return true when rule is successfully removed");

        // Verify route fails after unregistering
        var rawAfter = new RawPacket("COM1", "1500\r\n");
        var packetsAfter = router.Route(rawAfter);
        packetsAfter.Should().BeEmpty("DataRouter should no longer match unregistered rule");
    }

    [Fact]
    public void Fix5_XorChecksum_AppendChecksum_StripsLeadingDollar_NoDoubleDollar()
    {
        string payloadWithDollar = "$DAB,10.5,20.3";
        string formatted = XorChecksum.AppendChecksum(payloadWithDollar);

        formatted.Should().NotStartWith("$$", "AppendChecksum must strip any leading '$' before prepending '$'");
        formatted.Should().StartWith("$DAB,10.5,20.3*");

        // Validate formatting with ValidateSpan
        bool isValid = XorChecksum.ValidateSpan(formatted.AsSpan(), out var content);
        isValid.Should().BeTrue("Output of AppendChecksum must pass ValidateSpan checksum verification");
        content.ToString().Should().Be("DAB,10.5,20.3");
    }
}
