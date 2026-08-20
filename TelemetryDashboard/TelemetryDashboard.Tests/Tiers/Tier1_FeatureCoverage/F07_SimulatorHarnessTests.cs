using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

/// <summary>
/// The mock serial device the simulator tests are built on.
/// </summary>
/// <remarks>
/// Named for the dual-MCU simulator until that engine was retired, which made the file name
/// describe a class that no longer exists — and it never tested that class anyway. What it covers
/// is <c>MockSerialDevice</c>: connect, disconnect, push, read back. That is worth keeping, because
/// several other suites trust this double to behave, and a double that quietly stops working makes
/// every test built on it pass for the wrong reason.
/// <para>
/// The profile-driven simulator itself is covered in <c>ProfileSimulatorTests</c>.
/// </para>
/// </remarks>
public class F07_SimulatorHarnessTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void TwoDevicesOpenIndependently()
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
    public async Task TheDoubleProducesTheLinesItWasAskedFor()
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
    public void APushedLineComesBackVerbatim()
    {
        var device = new MockSerialDevice("COM3", 115200);
        device.Connect();

        string rawHex = "$HEX,NODE_1,414243313233*00";
        device.PushLine(rawHex);

        var lines = device.ReadAvailableLines();
        lines.Should().Contain(rawHex);
    }

    // Removed: a test that built a noisy value from System.Random inside its own body and asserted
    // the result was in range. It exercised no line of this repository -- it asserted that
    // Random.NextDouble returns something between 0 and 1 -- so it could never fail for a reason
    // anyone would want to know about. Bounded generation is covered where it is actually
    // implemented, in ProfileSimulatorTests.EveryValueStaysInsideTheRangeTheProfileDeclared.

    [Fact]
    [Trait("Category", "Tier1")]
    public void DisconnectClosesThePort()
    {
        var device = new MockSerialDevice("COM3", 115200);
        device.Connect();
        device.Disconnect();

        device.IsOpen.Should().BeFalse();
    }
}
