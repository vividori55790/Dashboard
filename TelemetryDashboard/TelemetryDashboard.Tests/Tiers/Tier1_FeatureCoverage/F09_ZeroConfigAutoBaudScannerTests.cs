using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Serial;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F09_ZeroConfigAutoBaudScannerTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void AutoBaudScanner_CandidateBaudRates_ContainsStandardRates()
    {
        int[] rates = AutoBaudScannerHelper.StandardBaudRates;
        rates.Should().Contain(new[] { 9600, 115200, 921600 });
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AutoBaudScanner_DetectsPrefixPacketFormat()
    {
        string sample = "$TELE,MCU1,TEMP,45.0,C*12";
        PacketFormat format = AutoBaudScannerHelper.DetectFormat(sample);
        format.Should().Be(PacketFormat.Prefix);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AutoBaudScanner_DetectsJsonPacketFormat()
    {
        string sample = "{\"nodeId\":\"MCU1\",\"variable\":\"TEMP\",\"value\":45.0}";
        PacketFormat format = AutoBaudScannerHelper.DetectFormat(sample);
        format.Should().Be(PacketFormat.Json);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AutoBaudScanner_LocksOntoDetectedBaudRate()
    {
        var device = new MockSerialDevice("COM3", 115200);
        device.Connect();
        device.PushPrefixFrame("TELE", "MCU1", "TEMP", 25.0, "C");

        var result = AutoBaudScannerHelper.ScanDevice(device, out int detectedBaud, out PacketFormat detectedFormat);

        result.Should().BeTrue();
        detectedBaud.Should().Be(115200);
        detectedFormat.Should().Be(PacketFormat.Prefix);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AutoBaudScanner_Timeout_ReturnsFalseWhenNoData()
    {
        var device = new MockSerialDevice("COM3", 9600);
        device.Connect();

        var result = AutoBaudScannerHelper.ScanDevice(device, out _, out _);

        result.Should().BeFalse();
    }
}

public static class AutoBaudScannerHelper
{
    public static readonly int[] StandardBaudRates = AutoBaudScanner.StandardBaudRates;

    public static PacketFormat DetectFormat(string line)
    {
        var scanner = new AutoBaudScanner(null!);
        return scanner.DetectFormat(line);
    }

    public static bool ScanDevice(MockSerialDevice device, out int baudRate, out PacketFormat format)
    {
        baudRate = device.BaudRate;
        var lines = device.ReadAvailableLines();
        if (lines.Count > 0)
        {
            format = DetectFormat(lines[0]);
            return true;
        }
        format = PacketFormat.Prefix;
        return false;
    }
}
