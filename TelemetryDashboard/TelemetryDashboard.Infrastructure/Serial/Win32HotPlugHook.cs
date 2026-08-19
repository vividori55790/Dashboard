namespace TelemetryDashboard.Infrastructure.Serial;

using System.Runtime.InteropServices;
using TelemetryDashboard.Core.Events;

public static class Win32Native
{
    public const int WM_DEVICECHANGE = 0x0219;
    public const int DBT_DEVICEARRIVAL = 0x8000;
    public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    public const int DBT_DEVTYP_PORT = 0x00000003;

    [StructLayout(LayoutKind.Sequential)]
    public struct DEV_BROADCAST_HDR
    {
        public int dbch_size;
        public int dbch_devicetype;
        public int dbch_reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct DEV_BROADCAST_PORT
    {
        public int dbcp_size;
        public int dbcp_devicetype;
        public int dbcp_reserved;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string dbcp_name;
    }
}

public class Win32HotPlugHook : IDisposable
{
    private readonly Timer _debounceTimer;
    private readonly object _lock = new();
    private DeviceChangeType _lastChangeType;
    private string? _lastPortName;

    private DateTime _lastEventTime = DateTime.MinValue;
    private const int DebounceMs = 200;

    public event EventHandler<DeviceChangeEventArgs>? DeviceChanged;

    public Win32HotPlugHook()
    {
        _debounceTimer = new Timer(OnDebounceTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    public Win32HotPlugHook(IntPtr hwnd)
        : this()
    {
    }

    public IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Native.WM_DEVICECHANGE)
        {
            int eventType = wParam.ToInt32();

            if (eventType == Win32Native.DBT_DEVICEARRIVAL || eventType == Win32Native.DBT_DEVICEREMOVECOMPLETE)
            {
                DeviceChangeType changeType = (eventType == Win32Native.DBT_DEVICEARRIVAL)
                    ? DeviceChangeType.Arrival
                    : DeviceChangeType.Removal;

                string? portName = ExtractPortName(lParam);

                bool shouldFire = false;
                lock (_lock)
                {
                    DateTime now = DateTime.UtcNow;
                    if ((now - _lastEventTime).TotalMilliseconds >= DebounceMs || _lastChangeType != changeType || _lastPortName != portName)
                    {
                        _lastEventTime = now;
                        _lastChangeType = changeType;
                        _lastPortName = portName;
                        shouldFire = true;
                    }
                }

                if (shouldFire)
                {
                    DeviceChanged?.Invoke(this, new DeviceChangeEventArgs(changeType, portName));
                }
            }
        }
        return IntPtr.Zero;
    }

    private string? ExtractPortName(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero) return null;

        try
        {
            var hdr = Marshal.PtrToStructure<Win32Native.DEV_BROADCAST_HDR>(lParam);
            if (hdr.dbch_devicetype == Win32Native.DBT_DEVTYP_PORT)
            {
                var portStruct = Marshal.PtrToStructure<Win32Native.DEV_BROADCAST_PORT>(lParam);
                return portStruct.dbcp_name; // e.g. "COM3"
            }
        }
        catch
        {
            // Fallback if structure unmarshalling fails
        }
        return null;
    }

    private void OnDebounceTimerElapsed(object? state)
    {
        DeviceChangeType type;
        string? port;

        lock (_lock)
        {
            type = _lastChangeType;
            port = _lastPortName;
        }

        try
        {
            DeviceChanged?.Invoke(this, new DeviceChangeEventArgs(type, port));
        }
        catch { }
    }

    public void SimulateDeviceChange(DeviceChangeType changeType, string? portName = null)
    {
        DeviceChanged?.Invoke(this, new DeviceChangeEventArgs(changeType, portName));
    }

    public void Dispose()
    {
        _debounceTimer.Dispose();
    }
}
