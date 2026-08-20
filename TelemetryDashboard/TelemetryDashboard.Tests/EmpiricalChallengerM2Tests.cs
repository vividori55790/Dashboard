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
        using var simulator = new ProfileSimulatorEngine(MonitoringProfileLibrary.Generic);

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

                // Was "COM3 or COM4" -- the two ports of one customer's rig, which a profile-driven
                // simulator has no reason to know. What the toggling could actually damage is the
                // frame itself, so that is what gets checked: a half-written or mis-checksummed
                // frame after 50 start/stop cycles is the defect this test exists to catch.
                packet.PortName.Should().Be(simulator.PortName);
                XorChecksum.ValidateSpan(packet.RawLine.AsSpan(), out _).Should().BeTrue(
                    "rapid toggling must not be able to emit a torn frame");
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
        using var simulator = new ProfileSimulatorEngine(MonitoringProfileLibrary.Generic);
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
        using var simulator = new ProfileSimulatorEngine(MonitoringProfileLibrary.Generic);
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

        // This used to assert that both of one rig's ports appeared, whose real point was that the
        // generator covers every source it declares rather than starving all but the first. Stated
        // against the profile, that property survives the rig: every channel the profile declares
        // has to turn up in the stream. A channel that silently never generates would leave a chart
        // that looks fine and a quantity nobody is watching.
        string[] declared = MonitoringProfileLibrary.Generic.Channels.Select(c => c.Id).ToArray();
        IEnumerable<string> seen = receivedPackets
            .Select(p => p.RawLine.Split(','))
            .Where(f => f.Length >= 3)
            .Select(f => f[2])
            .Distinct();

        seen.Should().BeEquivalentTo(declared);
    }

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public void F07_HexFrame_ChecksumIsVerifiedByTheParserNotAssumed()
    {
        // Rewritten when the customer-specific simulator was retired. It used to assert that the
        // simulator emitted a $HEX frame for MCU_NODE_2 on COM4 -- one installation's node names,
        // checked through a generator rather than through the code that has to read them. The
        // frame format is a parser concern, so it is now tested against the parser directly, with
        // a literal frame. That is stricter: a corrupt frame must be rejected, and no simulator
        // needs to exist for the assertion to hold.
        const string body = "HEX,ANY_NODE,414243313233";
        byte checksum = XorChecksum.Calculate(System.Text.Encoding.UTF8.GetBytes(body));
        string frame = $"${body}*{checksum:X2}";

        XorChecksum.ValidateSpan(frame.AsSpan(), out _).Should().BeTrue();

        // One flipped character has to fail, or the check above proves nothing.
        string tampered = frame.Replace("414243", "414244");
        XorChecksum.ValidateSpan(tampered.AsSpan(), out _).Should().BeFalse(
            "a frame whose payload changed under a checksum is corrupt, not merely unfamiliar");
    }

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public void F07_HistFrame_IsParsedAsHistoricalRatherThanCurrent()
    {
        // Also rewritten away from the retired simulator. What matters about a $HIST frame is that
        // whatever reads it knows the sample is old: presenting backfilled data as current is the
        // failure this format exists to prevent, and that is a property of the parser.
        long unixSeconds = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var raw = new RawPacket("ANY_PORT", $"$HIST,ANY_NODE,TEMP,41.90,{unixSeconds}");

        var rule = new RoutingRule
        {
            Id = "hist", RuleType = RuleType.Prefix, Tag = "HIST", Port = "*", TargetNodeId = string.Empty
        };

        PrefixParser.TryParse(raw, rule, out List<TelemetryPacket>? packets).Should().BeTrue();
        packets.Should().NotBeNullOrEmpty();
        packets!.Should().OnlyContain(p => p.Flags.HasFlag(PacketFlags.IsHistorical),
            "a backfilled sample presented as current is the failure this frame format exists to prevent");
    }

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
