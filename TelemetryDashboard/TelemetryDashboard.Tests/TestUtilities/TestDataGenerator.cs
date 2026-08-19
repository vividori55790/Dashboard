using System;
using System.Collections.Generic;
using System.Text;

namespace TelemetryDashboard.Tests.TestUtilities;

/// <summary>
/// Synthesizer for generating valid and invalid telemetry frame payloads,
/// edge cases, corrupted checksums, extreme values, and test data structures.
/// </summary>
public static class TestDataGenerator
{
    private static readonly Random _random = new(1337);

    /// <summary>
    /// Computes NMEA-style XOR checksum byte over characters in string.
    /// </summary>
    public static byte CalculateXorChecksum(string input)
    {
        byte checksum = 0;
        foreach (char c in input)
        {
            checksum ^= (byte)c;
        }
        return checksum;
    }

    /// <summary>
    /// Creates a valid PREFIX telemetry frame.
    /// Format: $[TAG],[NODE],[VAR],[VAL],[UNIT]*[XOR]
    /// </summary>
    public static string CreateValidPrefixFrame(
        string tag = "TELE",
        string nodeId = "MCU_NODE_1",
        string variable = "TEMP",
        double value = 45.5,
        string unit = "C")
    {
        var body = $"{tag},{nodeId},{variable},{value:F2},{unit}";
        byte xor = CalculateXorChecksum(body);
        return $"${body}*{xor:X2}";
    }

    /// <summary>
    /// Creates a valid $HIST timeline resync packet.
    /// Format: $HIST,[NODE],[VAR],[VAL],[TIMESTAMP]
    /// </summary>
    public static string CreateHistResyncPacket(
        string nodeId = "MCU_NODE_1",
        string variable = "TEMP",
        double value = 45.5,
        long? timestamp = null)
    {
        long ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var body = $"HIST,{nodeId},{variable},{value:F2},{ts}";
        byte xor = CalculateXorChecksum(body);
        return $"${body}*{xor:X2}";
    }

    /// <summary>
    /// Creates a valid $CMD command packet.
    /// Format: $CMD,[COMMAND],[ARG]
    /// </summary>
    public static string CreateCmdPacket(string command = "REQ_RESYNC", string arg = "0")
    {
        var body = $"CMD,{command},{arg}";
        byte xor = CalculateXorChecksum(body);
        return $"${body}*{xor:X2}";
    }

    /// <summary>
    /// Creates a valid JSON telemetry frame.
    /// </summary>
    public static string CreateValidJsonFrame(
        string nodeId = "MCU_NODE_1",
        string variable = "TEMP",
        double value = 45.5,
        string unit = "C",
        long? timestamp = null)
    {
        long ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return $"{{\"nodeId\":\"{nodeId}\",\"variable\":\"{variable}\",\"value\":{value:F2},\"unit\":\"{unit}\",\"timestamp\":{ts}}}";
    }

    /// <summary>
    /// Creates a valid COLUMNS (CSV) telemetry frame.
    /// Format: [NODE],[VAR],[VAL],[UNIT],[TIMESTAMP]
    /// </summary>
    public static string CreateValidColumnsFrame(
        string nodeId = "MCU_NODE_1",
        string variable = "TEMP",
        double value = 45.5,
        string unit = "C",
        long? timestamp = null)
    {
        long ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return $"{nodeId},{variable},{value:F2},{unit},{ts}";
    }

    /// <summary>
    /// Creates a corrupted PREFIX frame with intentionally invalid XOR checksum.
    /// </summary>
    public static string CreateCorruptedChecksumPrefixFrame(
        string tag = "TELE",
        string nodeId = "MCU_NODE_1",
        string variable = "TEMP",
        double value = 45.5,
        string unit = "C")
    {
        var body = $"{tag},{nodeId},{variable},{value:F2},{unit}";
        byte correctXor = CalculateXorChecksum(body);
        byte wrongXor = (byte)(correctXor ^ 0xFF); // Flips all bits
        return $"${body}*{wrongXor:X2}";
    }

    /// <summary>
    /// Creates a truncated PREFIX frame missing the asterisk or checksum.
    /// </summary>
    public static string CreateTruncatedPrefixFrame(string nodeId = "MCU_NODE_1")
    {
        return $"$TELE,{nodeId},TEMP,45.5";
    }

    /// <summary>
    /// Creates malformed JSON telemetry frame string.
    /// </summary>
    public static string CreateMalformedJsonFrame()
    {
        return "{\"nodeId\":\"MCU_NODE_1\", \"variable\": \"TEMP\", \"value\": }";
    }

    /// <summary>
    /// Generates a set of boundary/edge-case frames (empty, NaN, infinity, overflow, unicode).
    /// </summary>
    public static IEnumerable<string> GenerateBoundaryAndEdgeCaseFrames()
    {
        yield return ""; // Empty string
        yield return "   \t\r\n"; // Whitespace only
        yield return CreateValidPrefixFrame(value: double.NaN);
        yield return CreateValidPrefixFrame(value: double.PositiveInfinity);
        yield return CreateValidPrefixFrame(value: double.NegativeInfinity);
        yield return CreateValidPrefixFrame(value: -273.15); // Absolute zero
        yield return CreateValidPrefixFrame(value: 9999999.99); // Extreme value
        yield return CreateValidPrefixFrame(nodeId: "NODE_#1!_온도_Sensör", variable: "TEMP_αβγ"); // Unicode
        yield return new string('A', 100_000); // 100KB buffer overflow frame
        yield return "$INVALID_PREFIX_WITHOUT_CHECKSUM";
        yield return "{\"unclosed_json\": true, ";
        yield return "0,0,0,0,0,0,0,0,0,0"; // Raw numbers
    }

    /// <summary>
    /// Generates a list of valid PREFIX frames for bulk processing tests.
    /// </summary>
    public static List<string> GenerateValidPrefixFrameBatch(int count)
    {
        var list = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            var node = $"NODE_{i % 5}";
            var varName = i % 2 == 0 ? "TEMP" : "VIB";
            var val = 20.0 + (i * 0.1);
            var unit = i % 2 == 0 ? "C" : "G";
            list.Add(CreateValidPrefixFrame("TELE", node, varName, val, unit));
        }
        return list;
    }
}
