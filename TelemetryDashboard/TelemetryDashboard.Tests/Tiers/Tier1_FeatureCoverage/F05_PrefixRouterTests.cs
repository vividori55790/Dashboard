using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Parsers;
using TelemetryDashboard.Core.Services;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F05_PrefixRouterTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void XorChecksum_CalculateAndValidate_MatchesNmeaFormat()
    {
        string payload = "TELE,NODE_1,TEMP,25.50,C";
        byte expectedXor = TestDataGenerator.CalculateXorChecksum(payload);
        string formattedFrame = $"${payload}*{expectedXor:X2}";

        bool isValid = XorChecksum.ValidateSpan(formattedFrame.AsSpan(), out var content);

        isValid.Should().BeTrue();
        content.ToString().Should().Be(payload);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void PrefixParser_TryParse_ParsesValidPrefixPacket()
    {
        string frame = TestDataGenerator.CreateValidPrefixFrame("TELE", "MCU_1", "TEMP", 42.0, "C");
        var raw = new RawPacket { Payload = frame, Timestamp = DateTime.UtcNow };
        var rule = new RoutingRule
        {
            Format = PacketFormat.Prefix,
            Tag = "$TELE",
            TargetNodeId = "MCU_1",
            IndexMap = new Dictionary<int, string> { [0] = "TEMP" }
        };

        bool result = PrefixParser.TryParse(raw, rule, out var packets);

        result.Should().BeTrue();
        packets.Should().HaveCount(1);
        packets[0].NodeId.Should().Be("MCU_1");
        packets[0].Variable.Should().Be("TEMP");
        packets[0].Value.Should().Be(42.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void JsonParser_TryParse_ParsesValidJsonPacket()
    {
        string jsonFrame = TestDataGenerator.CreateValidJsonFrame("MCU_1", "VIB", 1.8, "G");
        var raw = new RawPacket { Payload = jsonFrame, Timestamp = DateTime.UtcNow };
        var rule = new RoutingRule
        {
            Format = PacketFormat.Json,
            TargetNodeId = "MCU_1"
        };

        bool result = JsonParser.TryParse(raw, rule, out var packets);

        result.Should().BeTrue();
        packets.Should().NotBeEmpty();
        packets[0].Variable.Should().Be("VIB");
        packets[0].Value.Should().Be(1.8);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ColumnsParser_TryParse_ParsesValidColumnsPacket()
    {
        string csvFrame = TestDataGenerator.CreateValidColumnsFrame("MCU_1", "RPM", 2400.0, "RPM");
        var raw = new RawPacket { Payload = csvFrame, Timestamp = DateTime.UtcNow };
        var rule = new RoutingRule
        {
            Format = PacketFormat.Columns,
            TargetNodeId = "MCU_1",
            IndexMap = new Dictionary<int, string> { [0] = "NODE", [1] = "VAR", [2] = "VAL" }
        };

        bool result = ColumnsParser.TryParse(raw, rule, out var packets);

        result.Should().BeTrue();
        packets.Should().NotBeEmpty();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void DataRouter_Route_DispatchesToRegisteredSubscribers()
    {
        var router = new DataRouter();
        var rule = new RoutingRule
        {
            Id = "rule_1",
            Format = PacketFormat.Prefix,
            Tag = "$TELE",
            TargetNodeId = "MCU_1",
            IndexMap = new Dictionary<int, string> { [0] = "TEMP" }
        };

        router.RegisterRule(rule);
        string frame = TestDataGenerator.CreateValidPrefixFrame("TELE", "MCU_1", "TEMP", 50.0, "C");
        var raw = new RawPacket { Payload = frame, Timestamp = DateTime.UtcNow };

        List<TelemetryPacket> routedPackets = router.Route(raw).ToList();

        routedPackets.Should().HaveCount(1);
        routedPackets[0].Value.Should().Be(50.0);
    }
}
