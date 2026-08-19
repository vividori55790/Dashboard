namespace TelemetryDashboard.Tests;

/// <summary>Self-tests for the shared fixture builders.</summary>
/// <remarks>
/// <c>WpfTestHelper_RunOnStaThread_ExecutesOnStaApartmentState</c> moved to
/// TelemetryDashboard.Tests.Desktop along with the helper it exercises. STA apartments exist only on
/// Windows, so the test would have been a guaranteed failure on the Linux agent this project now
/// targets — the remaining fixtures here are plain string and buffer manipulation and run anywhere.
/// </remarks>
public class TestUtilitiesTests
{
    [Fact]
    public void MockSerialDevice_PushPrefixFrame_GeneratesValidFrameWithChecksum()
    {
        var device = new MockSerialDevice("COM3", 115200);
        device.Connect();

        string? receivedLine = null;
        device.LineReceived += (sender, line) => receivedLine = line;

        var line = device.PushPrefixFrame("TELE", "NODE1", "TEMP", 45.2, "C");

        line.Should().StartWith("$TELE,NODE1,TEMP,45.20,C*");
        receivedLine.Should().Be(line);
        device.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void MockSerialDevice_ConnectDisconnect_FiresEvents()
    {
        var device = new MockSerialDevice("COM4", 9600);
        bool state = false;
        device.ConnectionStateChanged += (s, isConnected) => state = isConnected;

        device.Connect();
        state.Should().BeTrue();

        device.Disconnect();
        state.Should().BeFalse();
    }

    [Fact]
    public void TestDataGenerator_CreateValidPrefixFrame_CalculatesCorrectChecksum()
    {
        var frame = TestDataGenerator.CreateValidPrefixFrame("TELE", "MCU_1", "VIB", 1.25, "G");
        frame.Should().StartWith("$TELE,MCU_1,VIB,1.25,G*");

        byte checksum = TestDataGenerator.CalculateXorChecksum("TELE,MCU_1,VIB,1.25,G");
        frame.Should().EndWith($"*{checksum:X2}");
    }

    [Fact]
    public void TestDataGenerator_CreateCorruptedChecksumPrefixFrame_HasMismatchedChecksum()
    {
        var validFrame = TestDataGenerator.CreateValidPrefixFrame("TELE", "MCU_1", "VIB", 1.25, "G");
        var corruptFrame = TestDataGenerator.CreateCorruptedChecksumPrefixFrame("TELE", "MCU_1", "VIB", 1.25, "G");

        corruptFrame.Should().NotBe(validFrame);
    }

}
