namespace TelemetryDashboard.Tests;

using Xunit;
using FluentAssertions;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Parsers;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Plugins;

public class EmpiricalCoreVerificationTests
{
    #region 1. XorChecksum Tests

    [Fact]
    public void XorChecksum_Calculate_ComputesCorrectXorOverSpan()
    {
        string payload = "DAB,10.5,20.3";
        byte expected = 0;
        foreach (char c in payload)
        {
            expected ^= (byte)c;
        }

        byte actual = XorChecksum.Calculate(payload.AsSpan());
        actual.Should().Be(expected);
    }

    [Fact]
    public void XorChecksum_ValidateSpan_ValidatesPayloadWithChecksum()
    {
        string payload = "DAB,10.5,20.3";
        byte cs = XorChecksum.Calculate(payload.AsSpan());
        string fullLine = $"${payload}*{cs:X2}\r\n";

        bool isValid = XorChecksum.ValidateSpan(fullLine.AsSpan(), out var contentSpan);
        isValid.Should().BeTrue();
        contentSpan.ToString().Should().Be("DAB,10.5,20.3");
    }

    [Fact]
    public void XorChecksum_AppendChecksum_WhenPayloadStartsWithDollar_StripsLeadingDollar_ProducesValidChecksum()
    {
        string payloadWithDollar = "$DAB,10.5,20.3";
        string formatted = XorChecksum.AppendChecksum(payloadWithDollar);

        // Appending checksum to "$DAB..." should NOT create "$$DAB..."
        formatted.Should().StartWith("$DAB");
        formatted.Should().NotStartWith("$$");

        // ValidateSpan on "$DAB,10.5,20.3*XX"
        bool isValid = XorChecksum.ValidateSpan(formatted.AsSpan(), out _);
        isValid.Should().BeTrue("AppendChecksum must produce a valid payload passing ValidateSpan");
    }

    #endregion

    #region 2. PrefixParser Tests

    [Fact]
    public void PrefixParser_StandardPacket_ParsesCorrectly()
    {
        var rule = new RoutingRule
        {
            RuleType = RuleType.Prefix,
            Tag = "$DAB",
            TargetNodeId = "NODE_1",
            IndexMap = new Dictionary<int, string> { { 0, "Voltage" }, { 1, "Current" } }
        };

        string body = "DAB,12.5,2.4";
        byte cs = XorChecksum.Calculate(body.AsSpan());
        string raw = $"${body}*{cs:X2}\r\n";

        var rawPkt = new RawPacket("COM3", raw);
        bool success = PrefixParser.TryParse(rawPkt, rule, out var packets);

        success.Should().BeTrue();
        packets.Should().HaveCount(2);
        packets[0].Variable.Should().Be("Voltage");
        packets[0].Value.Should().Be(12.5);
        packets[1].Variable.Should().Be("Current");
        packets[1].Value.Should().Be(2.4);
    }

    [Fact]
    public void PrefixParser_HistoricalPacket_ParsesHistoricalFormat()
    {
        var rule = new RoutingRule { TargetNodeId = "FALLBACK" };
        string raw = "$HIST,MCU_NODE,Temp,45.2,1600000000\r\n";
        var rawPkt = new RawPacket("COM3", raw);

        bool success = PrefixParser.TryParse(rawPkt, rule, out var packets);

        success.Should().BeTrue();
        packets.Should().HaveCount(1);
        packets[0].NodeId.Should().Be("MCU_NODE");
        packets[0].Variable.Should().Be("Temp");
        packets[0].Value.Should().Be(45.2);
        packets[0].Flags.HasFlag(PacketFlags.IsHistorical).Should().BeTrue();
    }

    [Fact]
    public void PrefixParser_TagCollision_RuleTagTAG1_DoesNotMatchTAG10()
    {
        var rule = new RoutingRule
        {
            RuleType = RuleType.Prefix,
            Tag = "TAG1",
            TargetNodeId = "NODE_1",
            IndexMap = new Dictionary<int, string> { { 0, "Val1" }, { 1, "Val2" } }
        };

        // Packet is actually for TAG10, with values 10.5, 20.3
        string body = "TAG10,10.5,20.3";
        byte cs = XorChecksum.Calculate(body.AsSpan());
        string raw = $"${body}*{cs:X2}\r\n";

        var rawPkt = new RawPacket("COM3", raw);
        bool success = PrefixParser.TryParse(rawPkt, rule, out _);

        success.Should().BeFalse("Rule for TAG1 should NOT match TAG10 packet");
    }

    #endregion

    #region 3. JsonParser Tests

    [Fact]
    public void JsonParser_StandardJson_ParsesMappedProperties()
    {
        var rule = new RoutingRule
        {
            RuleType = RuleType.Json,
            TargetNodeId = "NODE_1",
            JsonMap = new Dictionary<string, string> { { "temp", "Temperature" }, { "vib", "Vibration" } }
        };

        string json = "{\"temp\": 45.2, \"vib\": 1.25}";
        var rawPkt = new RawPacket("COM3", json);

        bool success = JsonParser.TryParse(rawPkt, rule, out var packets);

        success.Should().BeTrue();
        packets.Should().HaveCount(2);
        packets.First(p => p.Variable == "Temperature").Value.Should().Be(45.2);
        packets.First(p => p.Variable == "Vibration").Value.Should().Be(1.25);
    }

    [Fact]
    public void JsonParser_RuleWithPatternTag_DevicePSFB_FailsToMatch()
    {
        var rule = new RoutingRule
        {
            RuleType = RuleType.Json,
            Tag = "device:PSFB",
            TargetNodeId = "NODE_1",
            JsonMap = new Dictionary<string, string> { { "temp", "Temperature" } }
        };

        string json = "{\"device\": \"PSFB\", \"temp\": 45.2}";
        var rawPkt = new RawPacket("COM3", json);

        bool success = JsonParser.TryParse(rawPkt, rule, out var packets);

        // Empirical check: JsonParser tries double.TryParse("PSFB") which fails,
        // so parsedValues does NOT contain "device", causing rule pattern check to FAIL.
        success.Should().BeTrue("Pattern tag device:PSFB should match JSON containing device: PSFB");
    }

    [Fact]
    public void JsonParser_RuleWithPatternTag_DoesNotCheckValueEquality()
    {
        var rule = new RoutingRule
        {
            RuleType = RuleType.Json,
            Tag = "code:100",
            TargetNodeId = "NODE_1",
            JsonMap = new Dictionary<string, string> { { "temp", "Temperature" } }
        };

        // JSON has code: 999 (NOT 100)
        string json = "{\"code\": 999, \"temp\": 45.2}";
        var rawPkt = new RawPacket("COM3", json);

        bool success = JsonParser.TryParse(rawPkt, rule, out var packets);

        // If JsonParser only checks parsedValues.ContainsKey("code"), it will match even though value is 999!
        success.Should().BeFalse("Tag code:100 should NOT match JSON with code: 999");
    }

    #endregion

    #region 4. ColumnsParser Tests

    [Fact]
    public void ColumnsParser_CsvInput_ParsesMappedColumns()
    {
        var rule = new RoutingRule
        {
            RuleType = RuleType.Columns,
            TargetNodeId = "NODE_1",
            IndexMap = new Dictionary<int, string> { { 0, "RPM" }, { 1, "Temp" } }
        };

        string csv = "1500, 75.4\r\n";
        var rawPkt = new RawPacket("COM3", csv);

        bool success = ColumnsParser.TryParse(rawPkt, rule, out var packets);

        success.Should().BeTrue();
        packets.Should().HaveCount(2);
        packets[0].Variable.Should().Be("RPM");
        packets[0].Value.Should().Be(1500);
        packets[1].Variable.Should().Be("Temp");
        packets[1].Value.Should().Be(75.4);
    }

    [Fact]
    public void ColumnsParser_WithChecksumSuffix_StripsChecksumAndParses()
    {
        var rule = new RoutingRule
        {
            RuleType = RuleType.Columns,
            TargetNodeId = "NODE_1",
            IndexMap = new Dictionary<int, string> { { 0, "RPM" }, { 1, "Temp" } }
        };

        string csv = "1500,75.4*3F";
        var rawPkt = new RawPacket("COM3", csv);

        bool success = ColumnsParser.TryParse(rawPkt, rule, out var packets);

        success.Should().BeTrue();
        packets.Should().HaveCount(2);
        packets[0].Value.Should().Be(1500);
        packets[1].Value.Should().Be(75.4);
    }

    #endregion

    #region 5. FormulaEvaluator Tests

    [Fact]
    public void FormulaEvaluator_BasicMathAndFunctions_EvaluatesCorrectly()
    {
        var eval = new FormulaEvaluator();
        var resolver = new Func<string, string, double>((node, varName) =>
        {
            if (varName == "vout") return 12.0;
            if (varName == "iout") return 2.5;
            return 0.0;
        });

        double power = eval.Evaluate("vout * iout", "NODE1", resolver);
        power.Should().Be(30.0);

        double absVal = eval.Evaluate("abs(-15.5)", "NODE1", resolver);
        absVal.Should().Be(15.5);

        double sqrtVal = eval.Evaluate("sqrt(16)", "NODE1", resolver);
        sqrtVal.Should().Be(4.0);

        double minVal = eval.Evaluate("min(10, 20)", "NODE1", resolver);
        minVal.Should().Be(10.0);

        double maxVal = eval.Evaluate("max(10, 20)", "NODE1", resolver);
        maxVal.Should().Be(20.0);
    }

    [Fact]
    public void FormulaEvaluator_CrossNodeVariableReferences_EvaluatesCorrectly()
    {
        var eval = new FormulaEvaluator();
        var resolver = new Func<string, string, double>((node, varName) =>
        {
            if (node == "NODE_A" && varName == "temp") return 50.0;
            if (node == "NODE_B" && varName == "temp") return 30.0;
            return 0.0;
        });

        double diff = eval.Evaluate("[NODE_A].temp - [NODE_B].temp", "LOCAL", resolver);
        diff.Should().Be(20.0);
    }

    [Fact]
    public void FormulaEvaluator_UnaryMinus_EvaluatesNegativeNumbers()
    {
        var eval = new FormulaEvaluator();
        var resolver = new Func<string, string, double>((node, varName) => 0.0);

        double result1 = eval.Evaluate("-5 + 3", "NODE1", resolver);
        result1.Should().Be(-2.0, "Unary minus -5 + 3 should equal -2");

        double result2 = eval.Evaluate("5 * -3", "NODE1", resolver);
        result2.Should().Be(-15.0, "Unary minus 5 * -3 should equal -15");
    }

    [Fact]
    public void FormulaEvaluator_OperatorPrecedence_PowerHasHigherPrecedenceThanMultiply()
    {
        var eval = new FormulaEvaluator();
        var resolver = new Func<string, string, double>((node, varName) => 0.0);

        double result = eval.Evaluate("2 * 3 ^ 2", "NODE1", resolver);
        // Standard math: 2 * (3^2) = 2 * 9 = 18.
        result.Should().Be(18.0, "Power operator ^ must have higher precedence than multiply *");
    }

    [Fact]
    public void FormulaEvaluator_AstCache_IsolationBetweenNodes()
    {
        var eval = new FormulaEvaluator();
        
        // Step 1: Evaluate expression "vout * 2" on NODE_A
        double resA = eval.Evaluate("vout * 2", "NODE_A", (n, v) => n == "NODE_A" ? 10.0 : 999.0);
        resA.Should().Be(20.0);

        // Step 2: Evaluate SAME expression "vout * 2" on NODE_B
        double resB = eval.Evaluate("vout * 2", "NODE_B", (n, v) => n == "NODE_B" ? 5.0 : 999.0);

        // If _astCache keys on expression only without currentNodeId, resB will look up NODE_A (returning 999 * 2 = 1998)
        resB.Should().Be(10.0, "AST cache must be isolated per currentNodeId or resolve variables dynamically at evaluation time");
    }

    #endregion
}
