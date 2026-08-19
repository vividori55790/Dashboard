namespace TelemetryDashboard.Tests;

using Xunit;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Parsers;
using TelemetryDashboard.Core.Services;

public class ChallengerM1_2EmpiricalStressTests
{
    // ==========================================
    // 1. Stress-test JsonParser edge cases
    // ==========================================

    [Fact]
    public void JsonParser_EmptyTag_MatchesValidJsonMapWithoutFiltering()
    {
        // Null tag
        var ruleNullTag = new RoutingRule
        {
            Id = "RULE_NULL_TAG",
            RuleType = RuleType.Json,
            Tag = null!,
            TargetNodeId = "NODE_1",
            JsonMap = new Dictionary<string, string> { { "temp", "Temperature" } }
        };

        var rawPacket = new RawPacket("COM1", "{\"temp\": 36.6, \"other\": 100}");
        bool successNull = JsonParser.TryParse(rawPacket, ruleNullTag, out var packetsNull);
        successNull.Should().BeTrue("JsonParser with null tag should accept matching JsonMap");
        packetsNull.Should().HaveCount(1);
        packetsNull[0].Variable.Should().Be("Temperature");
        packetsNull[0].Value.Should().Be(36.6);

        // Empty string tag
        var ruleEmptyTag = new RoutingRule
        {
            Id = "RULE_EMPTY_TAG",
            RuleType = RuleType.Json,
            Tag = "",
            TargetNodeId = "NODE_1",
            JsonMap = new Dictionary<string, string> { { "temp", "Temperature" } }
        };

        bool successEmpty = JsonParser.TryParse(rawPacket, ruleEmptyTag, out var packetsEmpty);
        successEmpty.Should().BeTrue("JsonParser with empty string tag should accept matching JsonMap");
        packetsEmpty.Should().HaveCount(1);
    }

    [Fact]
    public void JsonParser_EmptyStringPropertyAndTagValues_HandledSafely()
    {
        var rule = new RoutingRule
        {
            Id = "RULE_EMPTY_KEY",
            RuleType = RuleType.Json,
            Tag = "device:",
            TargetNodeId = "NODE_1",
            JsonMap = new Dictionary<string, string> { { "temp", "Temperature" } }
        };

        string jsonWithEmptyTagVal = "{\"device\": \"\", \"temp\": 22.4}";
        var rawPacket = new RawPacket("COM1", jsonWithEmptyTagVal);

        bool success = JsonParser.TryParse(rawPacket, rule, out var packets);
        success.Should().BeTrue("JsonParser should match tag 'device:' when JSON device property is empty string ''");
        packets.Should().HaveCount(1);
        packets[0].Value.Should().Be(22.4);
    }

    [Fact]
    public void JsonParser_BooleanValues_ParsedAndDiscriminatorMatched()
    {
        var ruleBooleanKeyVal = new RoutingRule
        {
            Id = "RULE_BOOL_KV",
            RuleType = RuleType.Json,
            Tag = "is_active:true",
            TargetNodeId = "NODE_BOOL",
            JsonMap = new Dictionary<string, string> { { "temp", "Temperature" } }
        };

        string jsonActive = "{\"is_active\": true, \"alarm\": false, \"temp\": 75.2}";
        var rawActive = new RawPacket("COM1", jsonActive);

        bool success = JsonParser.TryParse(rawActive, ruleBooleanKeyVal, out var packets);
        success.Should().BeTrue("JsonParser should support boolean tag discriminator 'is_active:true'");
        packets.Should().HaveCount(1);
        packets[0].Value.Should().Be(75.2);

        // False tag match
        var ruleAlarmFalse = new RoutingRule
        {
            Id = "RULE_BOOL_FALSE",
            RuleType = RuleType.Json,
            Tag = "alarm:false",
            TargetNodeId = "NODE_BOOL",
            JsonMap = new Dictionary<string, string> { { "temp", "Temperature" } }
        };

        bool successFalse = JsonParser.TryParse(rawActive, ruleAlarmFalse, out var packetsFalse);
        successFalse.Should().BeTrue("JsonParser should support boolean tag discriminator 'alarm:false'");
        packetsFalse.Should().HaveCount(1);

        // Value match tag "true"
        var ruleValueTag = new RoutingRule
        {
            Id = "RULE_BOOL_VAL",
            RuleType = RuleType.Json,
            Tag = "true",
            TargetNodeId = "NODE_BOOL",
            JsonMap = new Dictionary<string, string> { { "temp", "Temperature" } }
        };

        bool successValTag = JsonParser.TryParse(rawActive, ruleValueTag, out var packetsValTag);
        successValTag.Should().BeTrue("JsonParser single-value tag 'true' should match if any stringField is 'true'");
        packetsValTag.Should().HaveCount(1);
    }

    [Fact]
    public void JsonParser_MalformedJson_FailsGracefullyWithoutThrowingExceptions()
    {
        var rule = new RoutingRule
        {
            Id = "RULE_MALFORMED",
            RuleType = RuleType.Json,
            TargetNodeId = "NODE_1",
            JsonMap = new Dictionary<string, string> { { "temp", "Temperature" } }
        };

        string[] malformedInputs = new[]
        {
            "{\"temp\": 25.5,",           // Truncated trailing comma
            "{\"temp\": }",              // Missing value
            "{\"temp\": 25.5",            // Unclosed brace
            "[1, 2, 3]",                  // Array instead of object
            "Not JSON at all",           // Plain text
            "",                          // Empty string
            "{ \"temp\": NaN }",          // Invalid JSON number token NaN
            "{ \"temp\": 1.2.3 }",        // Invalid number format
            "{"                          // Single brace
        };

        foreach (var badJson in malformedInputs)
        {
            var raw = new RawPacket("COM1", badJson);
            Action act = () =>
            {
                bool success = JsonParser.TryParse(raw, rule, out var packets);
                success.Should().BeFalse($"JsonParser should return false for malformed payload: '{badJson}'");
                packets.Should().BeEmpty();
            };

            act.Should().NotThrow($"Malformed payload '{badJson}' must not throw unhandled exception");
        }
    }

    [Fact]
    public void JsonParser_SpacesAroundKeysAndColons_HandledCorrectly()
    {
        var rule = new RoutingRule
        {
            Id = "RULE_SPACES",
            RuleType = RuleType.Json,
            Tag = "device:PSFB",
            TargetNodeId = "NODE_SPACES",
            JsonMap = new Dictionary<string, string> { { "temp", "Temperature" }, { "vout", "Voltage" } }
        };

        // Standard formatting with spaces around colons/commas in JSON formatting (valid JSON)
        string jsonWithSpacesAroundTokens = "{ \"device\" : \"PSFB\" , \"temp\" : 55.4 , \"vout\" : 12.0 }";
        var rawPacket = new RawPacket("COM1", jsonWithSpacesAroundTokens);

        bool success = JsonParser.TryParse(rawPacket, rule, out var packets);
        success.Should().BeTrue("JsonParser should parse standard JSON formatted with spaces around tokens");
        packets.Should().HaveCount(2);

        // JSON where string key itself contains surrounding whitespace inside quotes: { " temp " : 55.4 }
        string jsonWithSpaceInKeyName = "{ \"device\": \"PSFB\", \" temp \": 55.4 }";
        var rawKeySpace = new RawPacket("COM1", jsonWithSpaceInKeyName);

        bool successKeySpace = JsonParser.TryParse(rawKeySpace, rule, out var packetsKeySpace);
        // Note: Utf8JsonReader preserves exact string literals. If key name has space inside quotes (" temp "), 
        // stringFields[" temp "] is stored. JsonMap["temp"] lookup won't match unless key matches.
        // TryParse returns false (or count 0) cleanly without crashing.
        packetsKeySpace.Should().NotContain(p => p.Variable == "Temperature");
    }

    // ==========================================
    // 2. Stress-test XorChecksum payload variations
    // ==========================================

    [Fact]
    public void XorChecksum_SingleDollarPayload_AppendsAndValidatesCorrectly()
    {
        string payloadSingleDollar = "$TEMP,45.2,12.0";
        string formatted = XorChecksum.AppendChecksum(payloadSingleDollar);

        formatted.Should().StartWith("$TEMP,45.2,12.0*");
        formatted.Should().NotStartWith("$$");

        bool isValid = XorChecksum.ValidateSpan(formatted.AsSpan(), out var contentSpan);
        isValid.Should().BeTrue("Single dollar payload appended checksum must validate successfully");
        contentSpan.ToString().Should().Be("TEMP,45.2,12.0");
    }

    [Fact]
    public void XorChecksum_DoubleDollarPayload_StripsDoubleDollarAndValidates()
    {
        string payloadDoubleDollar = "$$TEMP,45.2,12.0";
        string formatted = XorChecksum.AppendChecksum(payloadDoubleDollar);

        formatted.Should().StartWith("$TEMP,45.2,12.0*", "AppendChecksum must strip all leading '$' and output a single '$'");
        formatted.Should().NotStartWith("$$");

        bool isValid = XorChecksum.ValidateSpan(formatted.AsSpan(), out var contentSpan);
        isValid.Should().BeTrue("Output from AppendChecksum for double dollar input must pass ValidateSpan");
        contentSpan.ToString().Should().Be("TEMP,45.2,12.0");
    }

    [Fact]
    public void XorChecksum_NoDollarPayload_PrependDollarAndValidates()
    {
        string payloadNoDollar = "TEMP,45.2,12.0";
        string formatted = XorChecksum.AppendChecksum(payloadNoDollar);

        formatted.Should().StartWith("$TEMP,45.2,12.0*");

        bool isValid = XorChecksum.ValidateSpan(formatted.AsSpan(), out var contentSpan);
        isValid.Should().BeTrue("No dollar payload appended checksum must pass ValidateSpan");
        contentSpan.ToString().Should().Be("TEMP,45.2,12.0");
    }

    [Fact]
    public void XorChecksum_ValidateSpan_DoubleDollarRawLine_BehaverAnalysis()
    {
        // Construct raw line that retains double dollar directly: "$$TEMP,10*CS"
        string payload = "TEMP,10";
        byte cs = XorChecksum.Calculate(payload.AsSpan());
        string doubleDollarRawLine = $"$${payload}*{cs:X2}\r\n";

        // ValidateSpan checks raw line. Standard rawLine starting with "$$"
        bool isValid = XorChecksum.ValidateSpan(doubleDollarRawLine.AsSpan(), out var content);
        // Note: ValidateSpan checks rawLine.StartsWith("$") and slices 1..starIdx.
        // For "$$TEMP,10", content becomes "$TEMP,10" which includes one '$', causing XOR mismatch unless stripped.
        // Documenting behavior: raw line with double $$ fails ValidateSpan unless passed through AppendChecksum or normalized.
        isValid.Should().BeFalse("Direct unnormalized raw line starting with '$$' fails ValidateSpan due to extra '$' in content span");
    }

    [Fact]
    public void XorChecksum_DollarEdgeCases_ProcessedSafely()
    {
        string[] dollarEdgeCases = new[] { "$", "$$", "$$$", "$$$$", "" };

        foreach (var edgePayload in dollarEdgeCases)
        {
            Action act = () =>
            {
                string res = XorChecksum.AppendChecksum(edgePayload);
                res.Should().Be("$*00\r\n", $"Empty or dollar-only payload '{edgePayload}' should reduce to empty span checksum");
            };
            act.Should().NotThrow($"AppendChecksum('{edgePayload}') should not throw");
        }
    }

    // ==========================================
    // 3. Stress-test DataRouter.UnregisterRule concurrent registration/unregistration
    // ==========================================

    [Fact]
    public async Task DataRouter_UnregisterRule_ConcurrentRegistrationAndUnregistration_ThreadSafe()
    {
        var router = new DataRouter();
        var node = new SensorNode("NODE_STRESS", "Stress Node", "COM1", "Power");
        router.RegisterNode(node);

        const int numTasks = 16;
        const int opsPerTask = 500;

        var tasks = new List<Task>();

        for (int taskIdx = 0; taskIdx < numTasks; taskIdx++)
        {
            int tid = taskIdx;
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < opsPerTask; i++)
                {
                    string ruleId = $"RULE_{tid}_{i % 20}";

                    // 1. Register rule
                    var rule = new RoutingRule
                    {
                        Id = ruleId,
                        RuleType = RuleType.Prefix,
                        Tag = "$PWR",
                        TargetNodeId = "NODE_STRESS",
                        IndexMap = new Dictionary<int, string> { { 0, "vout" } }
                    };
                    router.RegisterRule(rule);

                    // 2. Route packet
                    var raw = new RawPacket("COM1", "$PWR,12.5*38\r\n");
                    var packets = router.Route(raw);

                    // 3. Unregister rule
                    bool removed = router.UnregisterRule(ruleId);
                    removed.Should().BeTrue();
                }
            }));
        }

        Func<Task> act = async () => await Task.WhenAll(tasks);
        await act.Should().NotThrowAsync("Concurrent RegisterRule, UnregisterRule, and Route must be completely thread-safe without throwing exceptions");
    }
}
