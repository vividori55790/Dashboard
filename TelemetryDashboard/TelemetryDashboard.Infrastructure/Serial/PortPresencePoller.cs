using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using TelemetryDashboard.Core.Events;

namespace TelemetryDashboard.Infrastructure.Serial;

/// <summary>
/// Detects serial ports appearing and disappearing by polling the port list.
/// </summary>
/// <remarks>
/// <see cref="Win32HotPlugHook"/> is driven by <c>WM_DEVICECHANGE</c>, which only arrives through a
/// Win32 message pump — in practice, the WPF shell. On macOS and Linux, and in the headless host on
/// any platform, nothing ever calls it, so <c>DeviceChanged</c> never fires and every feature built
/// on it goes quiet: auto-reconnect never notices a device coming back, and an operator watching a
/// cable they just replugged sees nothing happen. Silently, with no error to explain it.
///
/// Polling <see cref="SerialPort.GetPortNames"/> is the portable answer. It costs a directory read
/// of <c>/dev</c> or a registry lookup every couple of seconds, which is nothing next to a feature
/// that otherwise does not exist off Windows. Where the message pump <em>is</em> available it stays
/// the better source — it is immediate and it names the port — so this is a fallback, not a
/// replacement.
/// </remarks>
public sealed class PortPresencePoller : IDisposable
{
    private readonly Func<string[]> _enumeratePorts;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    private bool _primed;

    public const int DefaultIntervalMs = 2000;

    /// <param name="intervalMs">How often to compare the port list.</param>
    /// <param name="enumeratePorts">Overridable for tests; defaults to the real port list.</param>
    public PortPresencePoller(int intervalMs = DefaultIntervalMs, Func<string[]>? enumeratePorts = null)
    {
        if (intervalMs < 100)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalMs), intervalMs,
                "Polling faster than 100 ms burns CPU without detecting anything sooner.");
        }

        _enumeratePorts = enumeratePorts ?? SerialPort.GetPortNames;
        IntervalMs = intervalMs;
        _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public int IntervalMs { get; }

    public bool IsRunning { get; private set; }

    /// <summary>Ports observed at the last poll.</summary>
    public IReadOnlyCollection<string> KnownPorts
    {
        get { lock (_gate) return _known.ToArray(); }
    }

    public event EventHandler<DeviceChangeEventArgs>? DeviceChanged;

    /// <summary>
    /// Starts polling. The first pass records what is already present without reporting it as
    /// arrivals — every port existing at startup is the baseline, not news.
    /// </summary>
    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _timer.Change(0, IntervalMs);
    }

    public void Stop()
    {
        IsRunning = false;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Runs one comparison immediately. Exposed so a test need not wait for the timer.</summary>
    public void Poll()
    {
        string[] current;
        try
        {
            current = _enumeratePorts() ?? Array.Empty<string>();
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or UnauthorizedAccessException)
        {
            // A platform that cannot enumerate ports has no hot-plug story; stop rather than
            // raising the same failure every two seconds.
            Stop();
            return;
        }

        List<(DeviceChangeType Change, string Port)> changes = new();

        lock (_gate)
        {
            var seen = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);

            if (_primed)
            {
                foreach (string port in seen.Except(_known, StringComparer.OrdinalIgnoreCase))
                {
                    changes.Add((DeviceChangeType.Arrival, port));
                }
                foreach (string port in _known.Except(seen, StringComparer.OrdinalIgnoreCase))
                {
                    changes.Add((DeviceChangeType.Removal, port));
                }
            }

            _known = seen;
            _primed = true;
        }

        // Raised outside the lock: a handler that reconnects a port would otherwise block polling.
        foreach ((DeviceChangeType change, string port) in changes)
        {
            DeviceChanged?.Invoke(this, new DeviceChangeEventArgs(change, port));
        }
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }
}
