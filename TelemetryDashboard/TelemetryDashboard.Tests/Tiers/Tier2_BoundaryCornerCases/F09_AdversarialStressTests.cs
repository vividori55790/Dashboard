namespace TelemetryDashboard.Tests.Tiers.Tier2_BoundaryCornerCases;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Xunit;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Serial;

[Collection(HeavyTestCollection.Name)]
public class F09_AdversarialStressTests
{
    private readonly Mock<ISerialManager> _mockSerialManager;
    private readonly AutoBaudScanner _scanner;

    public F09_AdversarialStressTests()
    {
        _mockSerialManager = new Mock<ISerialManager>();
        var channel = System.Threading.Channels.Channel.CreateUnbounded<RawPacket>();
        _mockSerialManager.Setup(s => s.PacketReader).Returns(channel.Reader);
        _scanner = new AutoBaudScanner(_mockSerialManager.Object);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task Scenario1_Filtering_NegativeZeroEmptyBaudRates()
    {
        // Test with negative and zero candidate baud rates
        int[] invalidBauds = new int[] { -115200, -9600, 0, -1 };
        var result1 = await _scanner.ScanAsync("COM3", invalidBauds, CancellationToken.None);
        result1.IsSuccess.Should().BeFalse();
        result1.DetectedBaudRate.Should().Be(0);
        result1.DetectedFormat.Should().Be(PacketFormat.Unknown);
        _mockSerialManager.Verify(s => s.ConnectAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);

        // Test with empty array
        var result2 = await _scanner.ScanAsync("COM3", Array.Empty<int>(), CancellationToken.None);
        result2.IsSuccess.Should().BeFalse();
        result2.DetectedBaudRate.Should().Be(0);

        // Test with mixed array: negative/zero should be ignored, positive candidate tested
        _mockSerialManager.Setup(s => s.ConnectPortAsync("COM3", 115200, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var testChannel = System.Threading.Channels.Channel.CreateUnbounded<RawPacket>();
        testChannel.Writer.TryWrite(new RawPacket("COM3", "$TELE,MCU1,25.0*12"));
        _mockSerialManager.Setup(s => s.PacketReader).Returns(testChannel.Reader);

        var result3 = await _scanner.ScanAsync("COM3", new int[] { -9600, 0, 115200 }, CancellationToken.None);
        result3.IsSuccess.Should().BeTrue();
        result3.DetectedBaudRate.Should().Be(115200);
        result3.DetectedFormat.Should().Be(PacketFormat.Prefix);
        _mockSerialManager.Verify(s => s.ConnectPortAsync("COM3", -9600, It.IsAny<CancellationToken>()), Times.Never);
        _mockSerialManager.Verify(s => s.ConnectPortAsync("COM3", 0, It.IsAny<CancellationToken>()), Times.Never);
        _mockSerialManager.Verify(s => s.ConnectPortAsync("COM3", 115200, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void Scenario2_BinaryNoiseAndGarbageBytes_ReturnsUnknownFormat()
    {
        // Null and empty byte arrays
        _scanner.DetectFormat((byte[]?)null).Should().Be(PacketFormat.Unknown);
        _scanner.DetectFormat(Array.Empty<byte>()).Should().Be(PacketFormat.Unknown);

        // Byte array containing NULL byte (0x00)
        byte[] nullByteNoise = new byte[] { 0x00, 0x24, 0x48, 0x45, 0x58 };
        _scanner.DetectFormat(nullByteNoise).Should().Be(PacketFormat.Unknown);

        // Byte array containing control chars (0x07 bell, 0x1B escape, 0x7F delete)
        byte[] controlCharNoise = new byte[] { 0x07, 0x24, 0x54, 0x45, 0x4C, 0x45 };
        _scanner.DetectFormat(controlCharNoise).Should().Be(PacketFormat.Unknown);

        byte[] deleteNoise = new byte[] { 0x7F, 0x7B, 0x22, 0x61, 0x22, 0x3A, 0x31, 0x7D };
        _scanner.DetectFormat(deleteNoise).Should().Be(PacketFormat.Unknown);

        // Random non-ASCII binary noise bytes (>0x7F)
        byte[] highByteGarbage = new byte[] { 0x80, 0x90, 0xA0, 0xB0, 0xC0, 0xD0, 0xE0, 0xF0 };
        _scanner.DetectFormat(highByteGarbage).Should().Be(PacketFormat.Unknown);

        // High-byte noise containing commas
        byte[] commaHighNoise = new byte[] { 0x80, 0x2C, 0x81, 0x2C, 0x82 };
        _scanner.DetectFormat(commaHighNoise).Should().Be(PacketFormat.Unknown);

        // High-byte noise starting with dollar sign
        byte[] dollarHighNoise = new byte[] { 0x24, 0x80, 0x81, 0x82 };
        _scanner.DetectFormat(dollarHighNoise).Should().Be(PacketFormat.Unknown);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void Scenario3_MultiFormatClassification_Correctness()
    {
        // 1. $HEX format classification
        _scanner.DetectFormat("$HEX,MCU_NODE_2,414243313233*7E").Should().Be(PacketFormat.Hex);
        _scanner.DetectFormat("$hex,node1,deadbeef").Should().Be(PacketFormat.Hex);

        // 2. $PREFIX format classification
        _scanner.DetectFormat("$TELE,MCU_NODE_1,TEMP,25.5,C*12").Should().Be(PacketFormat.Prefix);
        _scanner.DetectFormat("$GPS,123456,37.5,127.0").Should().Be(PacketFormat.Prefix);

        // 3. {JSON} format classification
        _scanner.DetectFormat("{\"nodeId\":\"MCU1\",\"temp\":25.5}").Should().Be(PacketFormat.Json);
        _scanner.DetectFormat("   {\"a\": 1, \"b\": 2}   ").Should().Be(PacketFormat.Json);

        // 4. CSV COLUMNS format classification
        _scanner.DetectFormat("100, 200, 300").Should().Be(PacketFormat.Columns);
        _scanner.DetectFormat("NODE1, 25.5, 10.2, 88.0").Should().Be(PacketFormat.Columns);

        // 5. Unrecognized / Malformed payloads -> Unknown
        _scanner.DetectFormat((string?)null).Should().Be(PacketFormat.Unknown);
        _scanner.DetectFormat("   ").Should().Be(PacketFormat.Unknown);
        _scanner.DetectFormat("SINGLE_TOKEN").Should().Be(PacketFormat.Unknown);
        _scanner.DetectFormat("100, 200").Should().Be(PacketFormat.Unknown); // less than 3 columns
        _scanner.DetectFormat("   { incomplete json without closing brace").Should().Be(PacketFormat.Unknown);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task Scenario4_PortDisconnectAndTimeout_Behavior()
    {
        // Case 1: Cancellation token already cancelled before scan
        using var cancelledCts = new CancellationTokenSource();
        cancelledCts.Cancel();

        var result1 = await _scanner.ScanAsync("COM3", new[] { 9600, 115200 }, cancelledCts.Token);
        result1.IsSuccess.Should().BeFalse();
        result1.DetectedBaudRate.Should().Be(0);
        _mockSerialManager.Verify(s => s.ConnectAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);

        // Case 2: Port connection throws exception (e.g. UnauthorizedAccessException or IOException)
        _mockSerialManager.Setup(s => s.ConnectAsync("COM99", It.IsAny<int>()))
            .ThrowsAsync(new IOException("COM99 is not available"));

        var result2 = await _scanner.ScanAsync("COM99", new[] { 9600, 115200 }, CancellationToken.None);
        result2.IsSuccess.Should().BeFalse();
        result2.DetectedBaudRate.Should().Be(0);
        _mockSerialManager.Verify(s => s.DisconnectPortAsync("COM99"), Times.Exactly(2));

        // Case 3: ReadAllAsync produces no data (timeout / silence)
        _mockSerialManager.Setup(s => s.ConnectAsync("COM3", It.IsAny<int>())).ReturnsAsync(true);
        var emptyChannel = System.Threading.Channels.Channel.CreateUnbounded<RawPacket>();
        emptyChannel.Writer.Complete();
        _mockSerialManager.Setup(s => s.PacketReader).Returns(emptyChannel.Reader);

        var result3 = await _scanner.ScanAsync("COM3", new[] { 9600 }, CancellationToken.None);
        result3.IsSuccess.Should().BeFalse();
        result3.DetectedBaudRate.Should().Be(0);

        // Case 4: DisconnectPortAsync throws exception in finally block — should not crash ScanAsync
        _mockSerialManager.Setup(s => s.DisconnectPortAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Disconnect failed"));

        Func<Task> act = async () => await _scanner.ScanAsync("COM3", new[] { 9600 }, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    private static async IAsyncEnumerable<RawPacket> GetAsyncEnumerable(params RawPacket[] packets)
    {
        foreach (var packet in packets)
        {
            yield return packet;
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<RawPacket> GetEmptyAsyncEnumerableWithDelay(int delayMs)
    {
        await Task.Delay(delayMs);
        yield break;
    }
}
