using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Events;

using TelemetryDashboard.Infrastructure.Serial;

namespace TelemetryDashboard.UI;

/// <summary>
/// Keeping the link to the device, and saying so when it comes and goes.
/// </summary>
/// <remarks>
/// <see cref="AutoReconnectEngine"/> has existed for a long time and this window never used it. A
/// bumped USB cable therefore ended a session in the quietest possible way: the charts stopped
/// moving, the connection indicator went on saying connected, and the only evidence was the silence
/// watch reporting channel after channel going quiet — which reads like a machine shutting down
/// rather than a cable coming loose.
/// <para>
/// Pressing Connect on a port that is not there yet is the same story from the other end, and used
/// to be a single failed attempt and an error line. Both are now the same thing: the port is
/// watched, and it is opened whenever it is present.
/// </para>
/// </remarks>
public partial class MainWindow
{
    private AutoReconnectEngine? _reconnect;
    private string _watchedPort = string.Empty;
    private CancellationTokenSource? _linkReaderCts;

    /// <summary>Watches <paramref name="portName"/> and opens it whenever it is present.</summary>
    private void WatchPort(string portName, int baudRate)
    {
        _watchedPort = portName;

        if (_reconnect is null)
        {
            // The in-memory port is never in the machine's list, so the watchdog is told where to
            // look. Without that the one port on which a dropped link can be produced deliberately
            // is the one port the recovery could never be demonstrated on.
            Func<string[]>? ports = IsLoopback(portName) ? () => [portName] : null;

            _reconnect = new AutoReconnectEngine(Serial, TimeSpan.FromSeconds(2), ports);
            _reconnect.Reconnected += OnPortOpened;
            _reconnect.ReconnectFailed += OnPortRefused;
        }

        // The registered timestamp is what a resync asks the device to resend from, so it starts at
        // now rather than at zero: a port being watched before it has ever delivered anything has
        // no history worth asking for.
        _reconnect.StartMonitoring(portName, baudRate);
    }

    /// <summary>Stops watching, because the operator disconnected on purpose.</summary>
    private async Task StopWatchingPortAsync()
    {
        _watchedPort = string.Empty;

        if (_reconnect is null) return;

        _reconnect.Reconnected -= OnPortOpened;
        _reconnect.ReconnectFailed -= OnPortRefused;
        await _reconnect.StopMonitoringAsync();
        await _reconnect.DisposeAsync();
        _reconnect = null;
    }

    /// <summary>
    /// Advances the point a resync would ask from. Called for every packet that arrives.
    /// </summary>
    /// <remarks>
    /// The whole reason the engine tracked a timestamp, and nothing had ever set it. Left at the
    /// moment the port was registered, a link that dropped after eight hours would ask the device
    /// to resend eight hours of history — on a bench link that is not a recovery, it is a second
    /// outage.
    /// </remarks>
    private void NoteLinkActivity(DateTime timestampUtc)
    {
        if (_reconnect is null || _watchedPort.Length == 0) return;

        _reconnect.UpdateLastTimestamp(_watchedPort, timestampUtc);
    }

    private bool _portRefusalReported;

    /// <summary>Starts the packet reader once, and leaves it running across reconnects.</summary>
    /// <remarks>
    /// One loop rather than one per connection. The manager delivers every port's packets through
    /// a single channel, so a second reader started on reconnect would take half the frames and
    /// each would then reach only one of the two consumers.
    /// </remarks>
    private void StartLinkReader()
    {
        if (_linkReaderCts is not null) return;

        _linkReaderCts = new CancellationTokenSource();
        _serialReadCts = _linkReaderCts;
        CancellationToken token = _linkReaderCts.Token;
        _ = Task.Run(() => ProcessRealSerialPacketsAsync(token));
    }

    private void StopLinkReader()
    {
        _linkReaderCts?.Cancel();
        _linkReaderCts = null;
        _serialReadCts = null;
    }
}
