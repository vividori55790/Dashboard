using System;
using System.Threading;
using System.Windows;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.Infrastructure.Serial;

namespace TelemetryDashboard.UI;

/// <summary>
/// A port with no device behind it, so the serial path can be exercised without hardware.
/// </summary>
/// <remarks>
/// The headless host has had this since the emergency interlock needed proving: the interlock is
/// the only feature here that acts on the machine rather than watching it, it is refused without a
/// port, and on a workstation with no MCU the furthest anyone could get was "the relay reports
/// itself armed".
/// <para>
/// The shell had no equivalent, and everything downstream of a port was therefore unreachable on a
/// desk: the reconnect watchdog, the anomaly edges on the hardware path, the wire-rule draft, and
/// the transmit path the control panel writes through. Four features that could only ever be
/// checked by somebody who already had the machine.
/// </para>
/// <para>
/// What this is not is a second simulator. The frames come from the same
/// <see cref="ProfileSimulatorEngine"/> the virtual-MCU button uses, and then go in through a port
/// buffer and out through the reader, so the framing, the checksum, the routing rules and the
/// wire-name mapping all run on their real inputs. What is not being checked is the driver, the
/// cable and the device.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>The name this port is offered under. Matches the host's <c>--serial loopback</c>.</summary>
    public const string LoopbackPort = "loopback";

    private LoopbackSerialManager? _loopback;
    private CancellationTokenSource? _loopbackCts;
    private ProfileSimulatorEngine? _loopbackEngine;

    /// <summary>Whether <paramref name="portName"/> selects the in-memory port.</summary>
    public static bool IsLoopback(string? portName) =>
        string.Equals(portName?.Trim(), LoopbackPort, StringComparison.OrdinalIgnoreCase);

    /// <summary>Builds the in-memory port and starts feeding the active profile's frames into it.</summary>
    private void StartLoopback()
    {
        StopLoopback();

        _loopback = new LoopbackSerialManager();
        Serial = _loopback;

        // Synthetic, and it has to say so where it is stored as well as where it is displayed. The
        // frames travel the real serial path, which is the point, and that is exactly why the mark
        // matters here: without it the archive holds generated readings under ordinary node names
        // with nothing to distinguish them from a record of the machine. The headless host makes
        // the same declaration -- LoopbackTelemetrySource.IsSimulated is true.
        _dataRouter.SourceIsSimulated = true;

        _loopbackEngine = new ProfileSimulatorEngine(_activeProfile ?? MonitoringProfileSet.Default);
        _loopbackEngine.StartSimulation();

        // The console's control panel drives this engine, so a browser can move a setpoint and
        // watch it arrive back through the port -- which is the same loop the commissioning panel
        // offers, now with the serial path in the middle of it.
        PublishControlToConsole(_loopbackEngine);

        _loopbackCts = new CancellationTokenSource();
        _ = Task.Run(() => PumpLoopbackAsync(_loopbackCts.Token));
    }

    /// <summary>Stops the feed and returns the shell to the real serial stack.</summary>
    private void StopLoopback()
    {
        _loopbackCts?.Cancel();
        _loopbackCts = null;

        _loopbackEngine?.StopSimulation();
        _loopbackEngine?.Dispose();
        _loopbackEngine = null;

        _loopback?.Complete();
        _loopback = null;
        Serial = _hardwareSerial;

        // Back to unmarked, because the next thing this shell reads may be a real device and a
        // stale flag would stamp measurements as synthetic -- the same defect the other way round.
        _dataRouter.SourceIsSimulated = false;
    }

    /// <summary>
    /// Drops the in-memory link, so the recovery path can be proved without pulling a connector.
    /// </summary>
    /// <remarks>
    /// The recovery is the part of a link an engineer most needs to trust before leaving a rig
    /// unattended, and on a desk with no MCU there was no way to see it happen at all.
    /// </remarks>
    private void BtnDropLink_Click(object sender, RoutedEventArgs e)
    {
        if (_loopback is null)
        {
            ControlPanel.LogMessage("LINK",
                "링크 끊김 시험은 loopback 포트에서만 할 수 있습니다. 실제 포트는 케이블이 결정합니다.");
            return;
        }

        _loopback.FaultPort(LoopbackPort, "link dropped on request");
    }

    private async Task PumpLoopbackAsync(CancellationToken token)
    {
        ProfileSimulatorEngine? engine = _loopbackEngine;
        LoopbackSerialManager? port = _loopback;
        if (engine is null || port is null) return;

        try
        {
            await foreach (RawPacket raw in engine.StreamSimulatedPackets(token))
            {
                // The line, not the packet. It goes into the port's buffer and comes back out of
                // the reader, so what the ingest path receives is what a device would have sent.
                //
                // A refused delivery is not the end of the feed. The port is closed -- somebody
                // dropped the link deliberately -- and these frames are lost exactly as a device's
                // would be while a connector is out. When the watchdog reopens it, they resume.
                port.Deliver(LoopbackPort, raw.RawLine);
            }
        }
        catch (OperationCanceledException)
        {
            // The operator disconnected.
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
                ControlPanel.LogMessage("ERROR", $"루프백 급전이 멈췄습니다: {ex.Message}"));
        }
    }
}
