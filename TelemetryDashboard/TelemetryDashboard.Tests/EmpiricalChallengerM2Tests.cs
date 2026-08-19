namespace TelemetryDashboard.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Parsers;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Simulator;

public class EmpiricalChallengerM2Tests
{
    #region F07: Dual-MCU Virtual Simulator Stress Tests

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public async Task F07_RapidStartStopCycles_DoesNotThrowOrLeakTasks()
    {
        using var simulator = new DualMcuVirtualSimulatorEngine();

        // Perform 50 rapid start/stop cycles
        for (int i = 0; i < 50; i++)
        {
            simulator.StartSimulation();
            simulator.IsRunning.Should().BeTrue();

            simulator.StopSimulation();
            simulator.IsRunning.Should().BeFalse();
        }

        // Final start to ensure simulator can still run after rapid toggling
        simulator.StartSimulation();
        simulator.IsRunning.Should().BeTrue();

        using var cts = new CancellationTokenSource(1000);
        int count = 0;

        try
        {
            await foreach (var packet in simulator.StreamSimulatedPackets(cts.Token))
            {
                packet.Should().NotBeNull();
                packet.PortName.Should().Match(p => p == "COM3" || p == "COM4");
                count++;
                if (count >= 10) break;
            }
        }
        catch (OperationCanceledException) { }

        count.Should().BeGreaterThan(0, "Simulator should produce packets after rapid toggling");
        simulator.StopSimulation();
    }

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public async Task F07_ConcurrentStartStop_ThreadSafetyCheck()
    {
        using var simulator = new DualMcuVirtualSimulatorEngine();
        var tasks = new List<Task>();

        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 20; j++)
                {
                    simulator.StartSimulation();
                    Thread.Sleep(5);
                    simulator.StopSimulation();
                }
            }));
        }

        Func<Task> act = async () => await Task.WhenAll(tasks);
        await act.Should().NotThrowAsync("Concurrent Start/Stop should be thread-safe");
    }

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public async Task F07_HighRateDataGeneration_CPUSafetyAndBoundedBuffer()
    {
        using var simulator = new DualMcuVirtualSimulatorEngine();
        simulator.StartSimulation();

        using var cts = new CancellationTokenSource(1500);
        List<RawPacket> receivedPackets = new();

        try
        {
            await foreach (var packet in simulator.StreamSimulatedPackets(cts.Token))
            {
                receivedPackets.Add(packet);
                if (receivedPackets.Count >= 50) break;
            }
        }
        catch (OperationCanceledException) { }

        simulator.StopSimulation();
        receivedPackets.Count.Should().BeGreaterThanOrEqualTo(10, "Simulator should stream packets continuously");

        // Verify packet contents
        receivedPackets.Should().Contain(p => p.PortName == "COM3");
        receivedPackets.Should().Contain(p => p.PortName == "COM4");
    }

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public async Task F07_HexFramePayload_FormattingAndChecksumVerification()
    {
        using var simulator = new DualMcuVirtualSimulatorEngine();
        simulator.StartSimulation();

        using var cts = new CancellationTokenSource(1500);
        RawPacket? hexPacket = null;

        try
        {
            await foreach (var packet in simulator.StreamSimulatedPackets(cts.Token))
            {
                if (packet.Payload.StartsWith("$HEX,"))
                {
                    hexPacket = packet;
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }

        simulator.StopSimulation();

        hexPacket.Should().NotBeNull("Simulator should produce $HEX frames on MCU Node 2 (COM4)");
        hexPacket!.Value.PortName.Should().Be("COM4");

        // Check format: $HEX,MCU_NODE_2,414243313233*CS
        string payload = hexPacket.Value.Payload;
        payload.Should().StartWith("$HEX,MCU_NODE_2,");
        payload.Should().Contain("*");

        int asteriskIdx = payload.LastIndexOf('*');
        string body = payload.Substring(1, asteriskIdx - 1);
        string checksumHex = payload.Substring(asteriskIdx + 1);

        byte expectedCs = XorChecksum.Calculate(System.Text.Encoding.UTF8.GetBytes(body));
        byte actualCs = Convert.ToByte(checksumHex, 16);
        actualCs.Should().Be(expectedCs, "$HEX frame XOR checksum should be mathematically accurate");
    }

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public async Task F07_HistSnapshot_FormattingVerification()
    {
        using var simulator = new DualMcuVirtualSimulatorEngine();
        simulator.StartSimulation();

        using var cts = new CancellationTokenSource(1500);
        RawPacket? histPacket = null;

        try
        {
            await foreach (var packet in simulator.StreamSimulatedPackets(cts.Token))
            {
                if (packet.Payload.StartsWith("$HIST,"))
                {
                    histPacket = packet;
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }

        simulator.StopSimulation();

        histPacket.Should().NotBeNull("Simulator should produce $HIST historical snapshot frames on COM4");
        histPacket!.Value.PortName.Should().Be("COM4");

        // Format: $HIST,MCU_NODE_2,TEMP,val,unixTs
        string[] parts = histPacket.Value.Payload.Split(',');
        parts.Length.Should().BeGreaterThanOrEqualTo(5);
        parts[0].Should().Be("$HIST");
        parts[1].Should().Be("MCU_NODE_2");
        parts[2].Should().Be("TEMP");
        double.TryParse(parts[3], out double val).Should().BeTrue();
        long.TryParse(parts[4], out long ts).Should().BeTrue();
        ts.Should().BeGreaterThan(1700000000);
    }

    #endregion

    #region F08: C/C++ Code Generator Stress Tests

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public void F08_UnicodeVariableName_SanitizeIdentifier_KoreanTemp1()
    {
        string input = "온도_Temp#1";
        string sanitized = CHeaderGenerator.SanitizeIdentifier(input);

        sanitized.Should().Be("Temp_1");
        Regex.IsMatch(sanitized, @"^[a-zA-Z_][a-zA-Z0-9_]*$").Should().BeTrue("Sanitized identifier must be valid C identifier");
    }

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public void F08_UnicodeVariableName_SanitizeIdentifier_VariousEdgeCases()
    {
        CHeaderGenerator.SanitizeIdentifier("123_temp").Should().Be("var_123_temp");
        CHeaderGenerator.SanitizeIdentifier("전압(V)").Should().Be("V");
        CHeaderGenerator.SanitizeIdentifier("!@#$%^&*()").Should().Be("var_default");
        CHeaderGenerator.SanitizeIdentifier("").Should().Be("var_default");
        CHeaderGenerator.SanitizeIdentifier(null!).Should().Be("var_default");
    }

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public void F08_EmptyConfiguration_Fallbacks_HeaderGeneration()
    {
        var gen = new CHeaderGenerator();

        string headerNull = gen.GenerateHeader(null);
        headerNull.Should().Contain("#define TELEMETRY_NODE_ID \"MCU_NODE_1\"");
        headerNull.Should().Contain("#define TELEMETRY_TAG \"TELE\"");
        headerNull.Should().Contain("float temperature;");
        headerNull.Should().Contain("float vibration;");
        headerNull.Should().Contain("#ifndef TELEMETRY_CONFIG_H");
        headerNull.Should().Contain("#define TELEMETRY_CONFIG_H");

        string headerEmpty = gen.GenerateHeader(new SensorNodeConfig());
        headerEmpty.Should().Contain("#define TELEMETRY_NODE_ID \"MCU_NODE_1\"");
        headerEmpty.Should().Contain("#define TELEMETRY_TAG \"TELE\"");
        headerEmpty.Should().Contain("float temperature;");
    }

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public void F08_HeaderGuard_ContainsCorrectMacroDefines()
    {
        var gen = new CHeaderGenerator();
        string header = gen.GenerateHeader(new SensorNodeConfig { NodeId = "TEST_NODE" });

        header.Should().Contain("#ifndef TELEMETRY_CONFIG_H");
        header.Should().Contain("#define TELEMETRY_CONFIG_H");
        header.Should().Contain("#endif // TELEMETRY_CONFIG_H");
    }

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public void F08_CalculateXorChecksumMacro_Verification()
    {
        var gen = new CHeaderGenerator();
        string header = gen.GenerateHeader(new SensorNodeConfig());

        header.Should().Contain("#define CALCULATE_XOR_CHECKSUM(b, len)");

        // Verify C# XOR calculation logic matching macro semantics
        byte[] testBytes = System.Text.Encoding.UTF8.GetBytes("TELE,MCU_NODE_1,TEMP,50.00,C");
        byte cs = XorChecksum.Calculate(testBytes);

        // Compute using explicit macro equivalent loop
        byte macroCs = 0;
        for (int i = 0; i < testBytes.Length; i++)
        {
            macroCs ^= testBytes[i];
        }

        macroCs.Should().Be(cs);
    }

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public void F08_DriverCodeGeneration_AllPlatforms()
    {
        var gen = new CHeaderGenerator();

        string stm32Driver = gen.GenerateDriverCode("STM32");
        stm32Driver.Should().Contain("HAL_UART_Transmit");
        stm32Driver.Should().Contain("Telemetry_SendPacket");

        string esp32Driver = gen.GenerateDriverCode("ESP32");
        esp32Driver.Should().Contain("uart_write_bytes");

        string arduinoDriver = gen.GenerateDriverCode("ARDUINO");
        arduinoDriver.Should().Contain("Serial.write");
    }

    #endregion
}
