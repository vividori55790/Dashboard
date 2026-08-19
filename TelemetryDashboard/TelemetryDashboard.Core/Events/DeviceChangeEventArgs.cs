namespace TelemetryDashboard.Core.Events;

public enum DeviceChangeType
{
    Arrival,
    Removal,
    Removed = Removal
}

public class DeviceChangeEventArgs : EventArgs
{
    public DeviceChangeType ChangeType { get; }
    public string? PortName { get; }

    public DeviceChangeEventArgs(DeviceChangeType changeType, string? portName = null)
    {
        ChangeType = changeType;
        PortName = portName;
    }
}

public enum PortConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Faulted
}
