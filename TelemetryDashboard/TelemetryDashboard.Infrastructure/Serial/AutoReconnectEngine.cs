namespace TelemetryDashboard.Infrastructure.Serial;

using System.Collections.Concurrent;
using System.IO.Ports;
using TelemetryDashboard.Core.Events;
using TelemetryDashboard.Core.Interfaces;

public class AutoReconnectEngine : IAsyncDisposable, IDisposable
{
    private readonly ISerialManager _serialManager;
    private readonly Func<string[]> _enumeratePorts;
    private readonly ConcurrentDictionary<string, (int BaudRate, DateTime LastTimestamp)> _targetPorts = new();
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts = new();
    private Task? _monitorTask;
    private bool _isEnabled = true;
    private bool _isRunning;

    /// <summary>
    /// Packets received while a registered port is down, replayed on reconnect.
    /// Link state and the zero-loss buffer belong together; splitting them across layers is
    /// what left the old Core-side manager unable to actually heal anything.
    /// </summary>
    public ZeroLossPacketBuffer OfflineBuffer { get; } = new();

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public bool IsRunning => _isRunning;

    public TimeSpan ReconnectInterval { get; set; } = TimeSpan.FromSeconds(1);

    public int RetryIntervalMs
    {
        get => (int)ReconnectInterval.TotalMilliseconds;
        set => ReconnectInterval = TimeSpan.FromMilliseconds(Math.Max(0, value));
    }

    public int MaxRetries { get; set; } = 3;

    /// <summary>Raised on the reconnecting thread once a port is open again.</summary>
    /// <remarks>
    /// Added because a silent recovery is barely better than none. The engine reconnected, asked
    /// the device to resend, and told nobody -- so the shell could not restart its read loop, could
    /// not update the connection indicator, and the operator could not tell a link that had healed
    /// from one that was still down.
    /// </remarks>
    public event EventHandler<PortLinkEventArgs>? Reconnected;

    /// <summary>Raised when an attempt to reopen a port that is present did not succeed.</summary>
    public event EventHandler<PortLinkEventArgs>? ReconnectFailed;

    /// <param name="enumeratePorts">
    /// What counts as a port that is present. Injected for the same reason
    /// <see cref="PortPresencePoller"/> injects it: the reconnect loop's whole decision is "the
    /// port is back and we are not on it", and with a static call to the machine's own hardware
    /// that decision could never be exercised anywhere except on a bench with a real device.
    /// </param>
    public AutoReconnectEngine(
        ISerialManager serialManager, TimeSpan interval = default, Func<string[]>? enumeratePorts = null)
    {
        _serialManager = serialManager;
        _enumeratePorts = enumeratePorts ?? SerialPort.GetPortNames;
        ReconnectInterval = interval == default ? TimeSpan.FromSeconds(1) : interval;
        _timer = new PeriodicTimer(ReconnectInterval == TimeSpan.Zero ? TimeSpan.FromMilliseconds(10) : ReconnectInterval);
        _serialManager.DeviceChanged += OnDeviceChanged;
    }

    public AutoReconnectEngine(ISerialManager serialManager, int retryIntervalMs, int maxRetries = 3)
        : this(serialManager, TimeSpan.FromMilliseconds(Math.Max(0, retryIntervalMs)))
    {
        MaxRetries = maxRetries;
    }

    public void RegisterTargetPort(string portName, int baudRate, DateTime initialTimestamp = default)
    {
        DateTime timestamp = initialTimestamp == default ? DateTime.UtcNow : initialTimestamp;
        _targetPorts[portName] = (baudRate, timestamp);
    }

    public void UnregisterTargetPort(string portName)
    {
        _targetPorts.TryRemove(portName, out _);
    }

    public void UpdateLastTimestamp(string portName, DateTime timestamp)
    {
        if (_targetPorts.TryGetValue(portName, out var info))
        {
            _targetPorts[portName] = (info.BaudRate, timestamp);
        }
    }

    public void Start()
    {
        _isRunning = true;
        _monitorTask = MonitorLoopAsync(_cts.Token);
    }

    public void StartMonitoring(string portName, int baudRate = 115200)
    {
        RegisterTargetPort(portName, baudRate);
        if (!_isRunning)
        {
            Start();
        }
    }

    public async Task StopMonitoringAsync()
    {
        _isRunning = false;
        _cts.Cancel();
        if (_monitorTask != null)
        {
            try { await _monitorTask; } catch { }
        }
    }

    public async Task<bool> TryReconnectAsync(string portName, int baudRate, CancellationToken cancellationToken = default)
    {
        int attempts = Math.Max(1, MaxRetries);
        for (int i = 0; i < attempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                bool success = await _serialManager.ConnectAsync(portName, baudRate);
                if (success) return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Fallthrough to retry loop
            }

            if (i < attempts - 1 && RetryIntervalMs > 0)
            {
                await Task.Delay(RetryIntervalMs, cancellationToken);
            }
        }
        return false;
    }

    public async Task<bool> TryReconnectAndResyncAsync(string portName, int baudRate, DateTime lastTimestamp, CancellationToken cancellationToken = default)
    {
        bool reconnected = await _serialManager.ConnectPortAsync(portName, baudRate, cancellationToken);
        if (!reconnected)
        {
            ReconnectFailed?.Invoke(
                this, new PortLinkEventArgs(portName, baudRate, lastTimestamp, "the port did not open"));
            return false;
        }

        // The resync asks the device for everything since the last reading that actually arrived.
        // That timestamp is only right if somebody has been updating it: with nothing calling
        // UpdateLastTimestamp anywhere in the product it stayed at the moment the port was first
        // registered, so a link that dropped after eight hours asked for eight hours of history.
        string timestampStr = lastTimestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        string resyncCmd = "$CMD,REQ_RESYNC," + timestampStr + "\r\n";
        await _serialManager.WriteLineAsync(portName, resyncCmd, cancellationToken);

        Reconnected?.Invoke(this, new PortLinkEventArgs(portName, baudRate, lastTimestamp));
        return true;
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(cancellationToken))
            {
                if (!_isEnabled) continue;

                try
                {
                    await CheckAndReconnectPortsAsync(cancellationToken);
                }
                catch (Exception failure) when (failure is not OperationCanceledException)
                {
                    // Enumerating ports can fail on its own. A watchdog that stops watching is
                    // worse than one that reports a bad tick, because nothing else notices.
                    ReconnectFailed?.Invoke(
                        this, new PortLinkEventArgs(string.Empty, 0, DateTime.UtcNow, failure.Message));
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async void OnDeviceChanged(object? sender, DeviceChangeEventArgs e)
    {
        if (!_isEnabled) return;
        if (e.ChangeType == DeviceChangeType.Arrival)
        {
            await CheckAndReconnectPortsAsync(_cts.Token);
        }
    }

    private async Task CheckAndReconnectPortsAsync(CancellationToken cancellationToken)
    {
        string[] availablePorts = _enumeratePorts();
        HashSet<string> availableSet = new(availablePorts, StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in _targetPorts)
        {
            string targetPort = kvp.Key;
            int baudRate = kvp.Value.BaudRate;
            DateTime lastTime = kvp.Value.LastTimestamp;

            bool isCurrentlyConnected = _serialManager.ActivePorts.TryGetValue(targetPort, out var status)
                                         && status == PortConnectionStatus.Connected;

            if (!isCurrentlyConnected && availableSet.Contains(targetPort))
            {
                // Guarded per port, and this is the whole reason the watchdog works at all. Opening
                // a port somebody else holds throws rather than returning false, and one throw from
                // here used to leave MonitorLoopAsync -- which catches only cancellation -- so the
                // task ended, nothing was awaiting it, and the engine went quiet forever. Measured
                // on the running window: the first attempt against a held COM3 killed the loop, and
                // releasing the port a minute later reconnected nothing.
                try
                {
                    await TryReconnectAndResyncAsync(targetPort, baudRate, lastTime, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception failure)
                {
                    ReconnectFailed?.Invoke(
                        this, new PortLinkEventArgs(targetPort, baudRate, lastTime, failure.Message));
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _isRunning = false;
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        _timer.Dispose();
        _serialManager.DeviceChanged -= OnDeviceChanged;

        if (_monitorTask != null)
        {
            try { await _monitorTask; } catch { }
        }
        try { _cts.Dispose(); } catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
