using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
            _reconnect = new AutoReconnectEngine(_serialManager, TimeSpan.FromSeconds(2));
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

    private void OnPortOpened(object? sender, PortLinkEventArgs e)
    {
        // Raised on the engine's own thread; everything below touches the window.
        Dispatcher.Invoke(() =>
        {
            bool wasDown = !_isConnected;
            _isConnected = true;
            _dataRouter.SourceIsSimulated = false;
            ShowConnected(e.PortName, e.BaudRate);
            StartLinkReader();

            // Korean, because these lines land in the same log beside the connection messages an
            // operator already reads there, and a log that changes language mid-incident is one
            // more thing to decode at the moment there is least time for it.
            ControlPanel.LogMessage("LINK", wasDown
                ? $"{e.PortName} 링크가 복구되었습니다. {e.ResyncFromUtc:HH:mm:ss} UTC 이후 데이터를 "
                  + "다시 보내달라고 장치에 요청했습니다."
                : $"{e.PortName} 를 {e.BaudRate} baud 로 다시 열었습니다.");
        });
    }

    private void OnPortRefused(object? sender, PortLinkEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Once per transition, not once per attempt: the engine tries every two seconds and a
            // line each time would bury everything else in the log within a minute.
            if (!_portRefusalReported)
            {
                _portRefusalReported = true;
                ControlPanel.LogMessage("LINK",
                    $"{e.PortName} 는 있지만 열리지 않습니다. 계속 확인합니다." 
                    + (e.Reason.Length > 0 ? $" ({e.Reason})" : string.Empty));
            }
        });
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
