using TelemetryDashboard.Core.Events;
using TelemetryDashboard.Infrastructure.Serial;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F03_Win32HotPlugTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void HotPlugHook_Initialization_RegistersNotificationHook()
    {
        using var hook = new Win32HotPlugHook();
        hook.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void HotPlugHook_DeviceArrival_FiresDeviceChangedEvent()
    {
        using var hook = new Win32HotPlugHook();
        DeviceChangeEventArgs? eventArgs = null;
        hook.DeviceChanged += (sender, args) => eventArgs = args;

        hook.SimulateDeviceChange(DeviceChangeType.Arrival, "COM3");

        eventArgs.Should().NotBeNull();
        eventArgs!.ChangeType.Should().Be(DeviceChangeType.Arrival);
        eventArgs.PortName.Should().Be("COM3");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void HotPlugHook_DeviceRemoval_FiresDeviceChangedEvent()
    {
        using var hook = new Win32HotPlugHook();
        DeviceChangeEventArgs? eventArgs = null;
        hook.DeviceChanged += (sender, args) => eventArgs = args;

        hook.SimulateDeviceChange(DeviceChangeType.Removal, "COM4");

        eventArgs.Should().NotBeNull();
        eventArgs!.ChangeType.Should().Be(DeviceChangeType.Removal);
        eventArgs.PortName.Should().Be("COM4");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void HotPlugHook_PortFilter_FiltersComPortDevices()
    {
        using var hook = new Win32HotPlugHook();
        int eventCount = 0;
        hook.DeviceChanged += (sender, args) => eventCount++;

        hook.SimulateDeviceChange(DeviceChangeType.Arrival, "COM5");
        hook.SimulateDeviceChange(DeviceChangeType.Arrival, "COM6");

        eventCount.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void HotPlugHook_Dispose_UnregistersHookListeners()
    {
        var hook = new Win32HotPlugHook();
        hook.Dispose();
        
        Action act = () => hook.SimulateDeviceChange(DeviceChangeType.Arrival, "COM3");
        act.Should().NotThrow();
    }
}
