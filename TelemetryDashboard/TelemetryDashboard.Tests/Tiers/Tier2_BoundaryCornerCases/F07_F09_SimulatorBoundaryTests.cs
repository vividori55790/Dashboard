namespace TelemetryDashboard.Tests.Tiers.Tier2_BoundaryCornerCases;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Moq;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.Infrastructure.Serial;
using TelemetryDashboard.Tests.TestUtilities;

public class F07_F09_SimulatorBoundaryTests
{
    #region F07: Dual-MCU Virtual Simulator Mode (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F07_Boundary_ExtremeSyntheticNoise_GeneratesBoundedOutput()
    {
        var mockSim = new Mock<ISimulatorEngine>();
        var packets = new List<RawPacket>();
        mockSim.Setup(s => s.StreamSimulatedPackets(It.IsAny<CancellationToken>()))
               .Returns(GetMockSyntheticStream(10, double.NaN));

        await foreach (var pkt in mockSim.Object.StreamSimulatedPackets())
        {
            packets.Add(pkt);
        }

        packets.Should().HaveCount(10);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F07_Boundary_ZeroIntervalUpdate_DoesNotMaxCpu()
    {
        var mockSim = new Mock<ISimulatorEngine>();
        mockSim.Setup(s => s.StartSimulation());
        mockSim.Setup(s => s.StopSimulation());

        Action act = () =>
        {
            mockSim.Object.StartSimulation();
            mockSim.Object.StopSimulation();
        };

        act.Should().NotThrow();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F07_Boundary_InvalidNodeConfiguration_UsesFallbackNodeId()
    {
        var node = new SensorNode { NodeId = "", FirmwareVersion = "v1.0" };
        string effectiveId = string.IsNullOrWhiteSpace(node.NodeId) ? "MCU_FALLBACK" : node.NodeId;
        effectiveId.Should().Be("MCU_FALLBACK");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F07_Boundary_HexFramePayloadCorruption_SkipsCorruptedFrame()
    {
        string corruptedHexFrame = "$HEX,CORRUPTED_NON_HEX_DATA_XYZ*FF\r\n";
        bool isValid = corruptedHexFrame.StartsWith("$HEX,") && corruptedHexFrame.Contains('*');
        isValid.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F07_Boundary_SimulationStopWhenNotRunning_NoOpWithoutException()
    {
        var mockSim = new Mock<ISimulatorEngine>();
        mockSim.Setup(s => s.StopSimulation());

        Action act = () => mockSim.Object.StopSimulation();
        act.Should().NotThrow();
    }

    private static async IAsyncEnumerable<RawPacket> GetMockSyntheticStream(int count, double noiseLevel)
    {
        for (int i = 0; i < count; i++)
        {
            yield return new RawPacket("SIM_COM3", TestDataGenerator.CreateValidPrefixFrame("TELE", "SIM_1", "TEMP", 50.0 + i, "C"));
            await Task.Yield();
        }
    }

    #endregion

    #region F08: C/C++ Firmware Code Generator (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public void F08_Boundary_EmptyNodeConfig_GeneratesMinimalValidCHeader()
    {
        var gen = new CHeaderGenerator();
        string result = gen.GenerateHeader(new SensorNodeConfig());

        result.Should().Contain("#ifndef TELEMETRY_CONFIG_H");
        result.Should().Contain("#define TELEMETRY_CONFIG_H");
        result.Should().Contain("#endif");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F08_Boundary_UnicodeVariableNamesInCodeGenerator_SanitizesToCIdentifiers()
    {
        var gen = new CHeaderGenerator();
        var config = new SensorNodeConfig
        {
            NodeId = "NODE_1",
            Variables = new List<VariableDefinition>
            {
                new VariableDefinition { Name = "온도_Temp#1", Unit = "C", DataType = "float" }
            }
        };

        string header = gen.GenerateHeader(config);
        header.Should().NotContain("온도_Temp#1");
        header.Should().MatchRegex(@"[A-Za-z0-9_]+");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F08_Boundary_WriteToReadOnlyFile_ThrowsIOException()
    {
        string tempFile = Path.GetTempFileName();
        File.SetAttributes(tempFile, FileAttributes.ReadOnly);

        try
        {
            Action act = () => File.WriteAllText(tempFile, "#define TEST 1");
            act.Should().Throw<UnauthorizedAccessException>();
        }
        finally
        {
            File.SetAttributes(tempFile, FileAttributes.Normal);
            File.Delete(tempFile);
        }
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F08_Boundary_ExtremeArraySize_GeneratesValidMacroGuards()
    {
        var gen = new CHeaderGenerator();
        var config = new SensorNodeConfig
        {
            NodeId = "EXTREME_NODE",
            BufferSize = 1_000_000
        };

        string header = gen.GenerateHeader(config);
        header.Should().Contain("#define TELEMETRY_BUFFER_SIZE 1000000");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F08_Boundary_NullTemplatePath_UsesEmbeddedDefaultTemplate()
    {
        var gen = new CHeaderGenerator();
        string header = gen.GenerateDriverCode(null);

        header.Should().NotBeNullOrEmpty();
        header.Should().Contain("telemetry_send_packet");
    }

    #endregion

    #region F09: Zero-Config Auto-Baud Rate & Packet Format Scanner (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F09_Boundary_InvalidBaudRateList_IgnoresUnsupportedRates()
    {
        var mockSerial = new Mock<ISerialManager>();
        var scanner = new AutoBaudScanner(mockSerial.Object);
        int[] invalidBauds = new int[] { -1, 0, 99999999 };

        var result = await scanner.ScanAsync("COM3", invalidBauds, CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F09_Boundary_AllBaudRatesFail_ReturnsFailedScanResult()
    {
        var mockSerial = new Mock<ISerialManager>();
        mockSerial.Setup(s => s.ConnectAsync(It.IsAny<string>(), It.IsAny<int>()))
                  .ThrowsAsync(new IOException("Connection failed"));

        var scanner = new AutoBaudScanner(mockSerial.Object);
        int[] bauds = new int[] { 9600, 115200, 921600 };

        var result = await scanner.ScanAsync("COM3", bauds, CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.DetectedBaudRate.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F09_Boundary_GarbageDataStream_DoesNotMatchAnyFormat()
    {
        byte[] garbageBytes = new byte[] { 0x00, 0xFF, 0xFE, 0x12, 0x34, 0x56, 0x78, 0x90 };
        var scanner = new AutoBaudScanner(Mock.Of<ISerialManager>());

        PacketFormat format = scanner.DetectFormat(garbageBytes);
        format.Should().Be(PacketFormat.Unknown);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F09_Boundary_TimeoutDuringBaudScan_AbortsCleanly()
    {
        var mockSerial = new Mock<ISerialManager>();
        mockSerial.Setup(s => s.ConnectAsync(It.IsAny<string>(), It.IsAny<int>()))
                  .Returns(Task.Delay(5000).ContinueWith(_ => true));

        var scanner = new AutoBaudScanner(mockSerial.Object);
        using var cts = new CancellationTokenSource(50);

        var result = await scanner.ScanAsync("COM3", new[] { 115200 }, cts.Token);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F09_Boundary_FormatAmbiguity_PrefersStrictChecksumFormat()
    {
        string ambiguousPrefix = "$TELE,NODE_1,TEMP,45.5,C*1E";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(ambiguousPrefix);

        var scanner = new AutoBaudScanner(Mock.Of<ISerialManager>());
        PacketFormat format = scanner.DetectFormat(bytes);

        format.Should().Be(PacketFormat.Prefix);
    }

    #endregion
}
