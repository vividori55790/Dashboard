using System;
using TelemetryDashboard.Core.Events;
using TelemetryDashboard.Infrastructure.Serial;

namespace TelemetryDashboard.UI;

/// <summary>
/// What the operator is told when the link comes and goes.
/// </summary>
/// <remarks>
/// Every line here lands in the event log beside the connection messages, so they are written
/// in the same language: a log that changes language mid-incident is one more thing to decode
/// at the moment there is least time for it.
/// </remarks>
public partial class MainWindow
{
    /// <summary>
    /// The link went down without anybody asking it to.
    /// </summary>
    /// <remarks>
    /// Nothing subscribed to this before, on either manager, because the event was declared on one
    /// implementation rather than on the interface. So a cable pulled out of this application was
    /// announced by nothing: the charts stopped, the indicator went on saying connected, and the
    /// only evidence was the silence watch reporting channel after channel going quiet -- which
    /// reads as a machine shutting down rather than a connector coming loose.
    /// </remarks>
    private void OnPortLost(object? sender, SerialPortFaultEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_isConnected) return;

            _isConnected = false;
            ShowDisconnected();
            _portRefusalReported = false;

            ControlPanel.LogMessage("LINK",
                $"{e.PortName} 링크가 끊어졌습니다 — {e.Describe()}. 포트가 다시 나타나면 자동으로 연결합니다.");
        });
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
}
