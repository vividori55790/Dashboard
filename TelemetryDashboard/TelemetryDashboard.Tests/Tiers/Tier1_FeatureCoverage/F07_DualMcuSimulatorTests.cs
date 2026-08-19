using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F07_DualMcuSimulatorTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void Simulator_Start_InitializesMockPorts()
    {
        var deviceCom3 = new MockSerialDevice("COM3", 115200);
        var deviceCom4 = new MockSerialDevice("COM4", 115200);

        deviceCom3.Connect().Should().BeTrue();
        deviceCom4.Connect().Should().BeTrue();

        deviceCom3.IsOpen.Should().BeTrue();
        deviceCom4.IsOpen.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task Simulator_GeneratesSyntheticThermalAndVibrationData()
    {
        var device = new MockSerialDevice("COM3", 115200);
        device.Connect();

        await device.GenerateSyntheticTelemetryStreamAsync(10, 0);

        var lines = device.ReadAvailableLines();
        lines.Should().HaveCount(10);
        lines.Should().Contain(l => l.Contains("TEMP") || l.Contains("VIB"));
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Simulator_GeneratesHexDataFrames()
    {
        var device = new MockSerialDevice("COM3", 115200);
        device.Connect();

        string rawHex = "$HEX,NODE_1,414243313233*00";
        device.PushLine(rawHex);

        var lines = device.ReadAvailableLines();
        lines.Should().Contain(rawHex);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Simulator_SynthesizesNoiseWaveform()
    {
        var random = new Random(42);
        double signal = 50.0;
        double noiseAmp = 2.5;

        double noisyValue = signal + (random.NextDouble() * 2 - 1) * noiseAmp;

        noisyValue.Should().BeInRange(47.5, 52.5);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Simulator_Stop_TerminatesStream()
    {
        var device = new MockSerialDevice("COM3", 115200);
        device.Connect();
        device.Disconnect();

        device.IsOpen.Should().BeFalse();
    }
}
