using TelemetryDashboard.Core.Events;
using TelemetryDashboard.Infrastructure.Serial;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Covers the portable hot-plug path. The Win32 hook needs a message pump, so without this the
/// feature simply does not exist on macOS, on Linux, or in the headless host.
/// </summary>
public class PortPresencePollerTests
{
    private static PortPresencePoller PollerOver(Func<string[]> ports) =>
        new(intervalMs: 100, enumeratePorts: ports);

    [Fact]
    public void FirstPoll_TreatsExistingPortsAsBaseline_NotArrivals()
    {
        using var poller = PollerOver(() => new[] { "COM3", "COM4" });
        var seen = new List<DeviceChangeEventArgs>();
        poller.DeviceChanged += (_, e) => seen.Add(e);

        poller.Poll();

        // Ports present at startup are the baseline. Reporting them as arrivals would fire a
        // reconnect for every device that was already attached.
        seen.Should().BeEmpty();
        poller.KnownPorts.Should().BeEquivalentTo(new[] { "COM3", "COM4" });
    }

    [Fact]
    public void NewPort_IsReportedAsArrival()
    {
        string[] ports = { "COM3" };
        using var poller = PollerOver(() => ports);
        poller.Poll();

        var seen = new List<DeviceChangeEventArgs>();
        poller.DeviceChanged += (_, e) => seen.Add(e);

        ports = new[] { "COM3", "COM7" };
        poller.Poll();

        seen.Should().ContainSingle();
        seen[0].ChangeType.Should().Be(DeviceChangeType.Arrival);
        seen[0].PortName.Should().Be("COM7");
    }

    [Fact]
    public void VanishedPort_IsReportedAsRemoval()
    {
        string[] ports = { "COM3", "COM7" };
        using var poller = PollerOver(() => ports);
        poller.Poll();

        var seen = new List<DeviceChangeEventArgs>();
        poller.DeviceChanged += (_, e) => seen.Add(e);

        ports = new[] { "COM3" };
        poller.Poll();

        seen.Should().ContainSingle();
        seen[0].ChangeType.Should().Be(DeviceChangeType.Removal);
        seen[0].PortName.Should().Be("COM7");
    }

    [Fact]
    public void UnchangedPortList_ReportsNothing()
    {
        using var poller = PollerOver(() => new[] { "COM3" });
        poller.Poll();

        var seen = new List<DeviceChangeEventArgs>();
        poller.DeviceChanged += (_, e) => seen.Add(e);

        poller.Poll();
        poller.Poll();

        // A poll that fires on every tick would make the log useless and retrigger reconnects.
        seen.Should().BeEmpty();
    }

    [Fact]
    public void Replug_IsReportedAsRemovalThenArrival()
    {
        string[] ports = { "ttyUSB0" };
        using var poller = PollerOver(() => ports);
        poller.Poll();

        var seen = new List<DeviceChangeEventArgs>();
        poller.DeviceChanged += (_, e) => seen.Add(e);

        ports = Array.Empty<string>();
        poller.Poll();
        ports = new[] { "ttyUSB0" };
        poller.Poll();

        // The sequence auto-reconnect needs, and the one that never arrived off Windows.
        seen.Select(e => e.ChangeType).Should()
            .Equal(DeviceChangeType.Removal, DeviceChangeType.Arrival);
    }

    [Fact]
    public void PlatformThatCannotEnumerate_StopsInsteadOfFailingEveryTick()
    {
        using var poller = PollerOver(() => throw new PlatformNotSupportedException());
        poller.Start();

        poller.Poll();

        poller.IsRunning.Should().BeFalse("re-raising the same failure every 2s helps nobody");
    }

    [Fact]
    public void PollingFasterThanAHundredMilliseconds_IsRefused()
    {
        Action act = () => new PortPresencePoller(intervalMs: 10);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
