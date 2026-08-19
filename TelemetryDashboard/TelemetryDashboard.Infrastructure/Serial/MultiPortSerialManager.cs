namespace TelemetryDashboard.Infrastructure.Serial;

using System.Collections.Concurrent;
using System.IO.Ports;
using System.Threading.Channels;
using TelemetryDashboard.Core.Events;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;

public class MultiPortSerialManager : ISerialManager
{
    private readonly Channel<RawPacket> _channel;
    private readonly ConcurrentDictionary<string, SerialPortWorker> _workers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PortConnectionStatus> _portStatuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Win32HotPlugHook _hotPlugHook;

    public ChannelReader<RawPacket> PacketReader => _channel.Reader;
    public IReadOnlyDictionary<string, PortConnectionStatus> ActivePorts => _portStatuses;

    public event EventHandler<DeviceChangeEventArgs>? DeviceChanged;

    /// <summary>
    /// Creates a manager, enabling port polling where the Win32 message pump cannot reach.
    /// </summary>
    /// <remarks>
    /// <see cref="Win32HotPlugHook"/> only ever fires when a Win32 <c>WndProc</c> forwards
    /// <c>WM_DEVICECHANGE</c> to it — in practice, the WPF shell. On macOS and Linux nothing does,
    /// so hot-plug detection silently did not exist there: replug a cable and auto-reconnect never
    /// learns the device came back. Polling is enabled automatically off Windows. A **headless
    /// Windows** host has no pump either and should call <see cref="EnablePortPolling"/> itself;
    /// it is not automatic there because the desktop shell, which does have a pump, uses this same
    /// constructor and would then get both sources.
    /// </remarks>
    public MultiPortSerialManager() : this(new Win32HotPlugHook())
    {
        if (!OperatingSystem.IsWindows())
        {
            EnablePortPolling();
        }
    }

    /// <summary>True when device arrival and removal are actually being detected.</summary>
    /// <remarks>
    /// Lets a host state the truth rather than imply a capability. Without polling and without a
    /// message pump this is false, and features that wait for a device to reappear will wait
    /// forever — worth saying out loud instead of leaving an operator to discover it.
    /// </remarks>
    public bool HotPlugDetectionActive => _poller?.IsRunning ?? false;

    /// <summary>Starts polling the port list so device changes are detected without a message pump.</summary>
    public void EnablePortPolling(int intervalMs = PortPresencePoller.DefaultIntervalMs)
    {
        if (_poller is not null) return;

        _poller = new PortPresencePoller(intervalMs);
        _poller.DeviceChanged += (s, e) => DeviceChanged?.Invoke(this, e);
        _poller.Start();
    }

    private PortPresencePoller? _poller;

    public MultiPortSerialManager(Win32HotPlugHook hotPlugHook)
    {
        _hotPlugHook = hotPlugHook;
        _hotPlugHook.DeviceChanged += (s, e) => DeviceChanged?.Invoke(this, e);

        BoundedChannelOptions options = new(capacity: 50_000)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = true
        };
        _channel = Channel.CreateBounded<RawPacket>(options);
    }

    public async Task<bool> ConnectPortAsync(string portName, int baudRate = 115200, CancellationToken cancellationToken = default)
    {
        if (_workers.ContainsKey(portName))
        {
            return true;
        }

        PortConnectionStatus previous = _portStatuses.TryGetValue(portName, out PortConnectionStatus known)
            ? known
            : PortConnectionStatus.Disconnected;

        _portStatuses[portName] = PortConnectionStatus.Connecting;

        try
        {
            SerialPortWorker worker = new(portName, baudRate, _channel.Writer);
            worker.Faulted += OnWorkerFaulted;

            if (await worker.StartAsync(cancellationToken))
            {
                _workers[portName] = worker;
                _portStatuses[portName] = PortConnectionStatus.Connected;

                // A port coming back from Faulted is a recovery, not a first connection, and the
                // difference is the whole content of the message an operator wants to see.
                if (previous == PortConnectionStatus.Faulted) PortRecovered?.Invoke(this, portName);
                return true;
            }
        }
        catch
        {
            _portStatuses[portName] = PortConnectionStatus.Faulted;
        }

        _portStatuses[portName] = PortConnectionStatus.Disconnected;
        return false;
    }

    public Task<bool> ConnectAsync(string portName, int baudRate)
    {
        return ConnectPortAsync(portName, baudRate, CancellationToken.None);
    }

    public async Task DisconnectPortAsync(string portName)
    {
        if (_workers.TryRemove(portName, out var worker))
        {
            worker.Faulted -= OnWorkerFaulted;
            await worker.StopAsync();
        }
        _portStatuses[portName] = PortConnectionStatus.Disconnected;
    }

    public async Task DisconnectAllAsync()
    {
        var keys = _workers.Keys.ToList();
        foreach (var key in keys)
        {
            await DisconnectPortAsync(key);
        }
    }

    public async Task WriteLineAsync(string portName, string data, CancellationToken cancellationToken = default)
    {
        if (_workers.TryGetValue(portName, out var worker))
        {
            await worker.WriteLineAsync(data, cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAllAsync();
        _channel.Writer.TryComplete();
        _poller?.Dispose();
        _hotPlugHook.Dispose();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Raised when a port dies without being asked to stop.</summary>
    /// <remarks>
    /// The manager forgets a faulted worker rather than keeping it. That is what makes recovery
    /// possible: the connect path returns early for a port it already holds, so a dead worker left
    /// in the table would make every later reconnect attempt succeed instantly and silently while
    /// no bytes ever arrived again.
    /// </remarks>
    public event EventHandler<SerialPortFaultEventArgs>? PortFaulted;

    /// <summary>Raised when a port that had faulted is carrying data again.</summary>
    public event EventHandler<string>? PortRecovered;

    private void OnWorkerFaulted(object? sender, SerialPortFaultEventArgs e)
    {
        if (_workers.TryRemove(e.PortName, out SerialPortWorker? worker))
        {
            worker.Faulted -= OnWorkerFaulted;
        }

        _portStatuses[e.PortName] = PortConnectionStatus.Faulted;
        PortFaulted?.Invoke(this, e);
    }
}
