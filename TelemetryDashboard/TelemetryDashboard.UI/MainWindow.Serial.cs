using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.UI.Dialogs;
using TelemetryDashboard.UI.Docking;

namespace TelemetryDashboard.UI;

/// <summary>Hardware connection: port discovery, connect/disconnect, and the real-hardware ingest loop.</summary>
public partial class MainWindow
{
    private void BtnRefreshPorts_Click(object sender, RoutedEventArgs e)
    {
        PopulatePortAndBaud();
        ControlPanel.LogMessage("SYSTEM", "Serial ports refreshed.");
    }

    private void BtnAutoScan_Click(object sender, RoutedEventArgs e)
    {
        AutoBaudScanDialog dlg = new AutoBaudScanDialog { Owner = this };
        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.DiscoveredPort))
        {
            if (!CboPort.Items.Contains(dlg.DiscoveredPort))
            {
                CboPort.Items.Add(dlg.DiscoveredPort);
            }
            CboPort.SelectedItem = dlg.DiscoveredPort;
            CboBaudRate.SelectedItem = dlg.DiscoveredBaudRate;
            ControlPanel.LogMessage("SYSTEM", $"Auto-scanned and selected {dlg.DiscoveredPort} @ {dlg.DiscoveredBaudRate} Baud.");
        }
    }

    private async void BtnToggleConnect_Click(object sender, RoutedEventArgs e)
    {
        if (!_isConnected)
        {
            // No invented default. Connecting to "COM1" because nothing was selected produced a
            // failure that reads as a broken cable rather than as an empty port list.
            if (!CboPort.IsEnabled || CboPort.SelectedItem is null)
            {
                ControlPanel.LogMessage("WARN",
                    "이 컴퓨터가 보고한 직렬 포트가 없어 연결할 수 없습니다. 장치를 연결한 뒤 포트 목록을 새로 고치세요.");
                return;
            }

            string portName = CboPort.SelectedItem.ToString() ?? string.Empty;
            if (portName.Contains(' ')) portName = portName.Split(' ')[0]; // Strip any descriptive suffix

            int baudRate = SelectedBaudRate();

            // Stop the simulation stream unconditionally. The tick timer is started at launch to
            // give the web console live data, so gating this on _isSimulating (false at startup)
            // meant simulated and real frames were broadcast side by side on the same channels.
            _simTimer?.Stop();
            if (_isSimulating)
            {
                StopSimulator();
            }

            try
            {
                // The in-memory port has to exist before anything tries to open it, and it brings
                // its own frames: the profile's simulator feeds the port rather than the router.
                if (IsLoopback(portName)) StartLoopback();

                bool ok = await Serial.ConnectPortAsync(portName, baudRate);

                // Watched either way -- except the in-memory one, which cannot come or go, and
                // whose name a watchdog scanning the machine's ports would never find.
                if (!IsLoopback(portName)) WatchPort(portName, baudRate);

                if (ok)
                {
                    _isConnected = true;

                    // Explicit rather than assumed. The two sources share one router, and a stale
                    // simulated flag would stamp real measurements as synthetic — the mirror image
                    // of the defect this marking exists to prevent, and just as misleading.
                    //
                    // The in-memory port is the exception and declares itself: its frames are
                    // generated, and travelling the real serial path is precisely why they have to
                    // keep saying so.
                    _dataRouter.SourceIsSimulated = IsLoopback(portName);

                    ShowConnected(portName, baudRate);
                    ControlPanel.LogMessage("SYSTEM", $"하드웨어 포트 {portName} @ {baudRate} baud 연결됨.");
                    StartLinkReader();
                }
                else
                {
                    ControlPanel.LogMessage("LINK",
                        $"{portName} 를 지금은 열 수 없습니다. 포트가 나타나면 자동으로 연결합니다.");
                }
            }
            catch (Exception ex)
            {
                ControlPanel.LogMessage("ERROR", $"Serial connection error: {ex.Message}");
            }
        }
        else
        {
            _isConnected = false;

            // The watchdog is stopped before the port is closed, or it would see a port that is
            // present and not connected and immediately undo what the operator just asked for.
            await StopWatchingPortAsync();
            StopLinkReader();
            await Serial.DisconnectAllAsync();
            StopLoopback();

            ShowDisconnected();
            ControlPanel.LogMessage("SYSTEM", "시리얼 포트 연결을 해제했습니다.");
        }
    }

    private async Task ProcessRealSerialPacketsAsync(CancellationToken token)
    {
        try
        {
            await foreach (var rawPacket in Serial.PacketReader.ReadAllAsync(token))
            {
                List<TelemetryPacket> packets = ResolvePackets(rawPacket);

                // What a resync after a drop would ask the device to resend from.
                NoteLinkActivity(rawPacket.Timestamp);

                foreach (var pkt in packets)
                {
                    var ml = _mlEngine.AnalyzeChannel($"{pkt.NodeId}.{pkt.Variable}", pkt.Value);

                    // Before the dispatcher hop, and through the same call the simulated stream
                    // uses. Hardware readings previously reached the CSV but never the durable
                    // archive, so the MATLAB export -- which reads only the archive -- returned
                    // nothing for every deployment that had actual hardware attached.
                    PersistSample(pkt, ml);

                    // Awaited rather than blocked on, which is what the simulated stream has always
                    // done. Dispatcher.Invoke holds the reader thread until the UI thread is free,
                    // so at any rate above a slow device the ingest loop spends its life waiting on
                    // the chart -- and the UI, being handed one work item per sample with a caller
                    // blocked behind each, stops answering anything else. Measured the first time a
                    // port carried more than a trickle: the window went unresponsive to automation
                    // within seconds of connecting.
                    await Dispatcher.InvokeAsync(() =>
                    {
                        // Plot only the channel that actually arrived. The previous code padded the
                        // scope and the statistics with literals (50.0 humidity, 0.1 vibration,
                        // 1200 rpm) whenever a temperature packet came in, so the operator saw
                        // invented readings for sensors the device had never reported.
                        ScopeControl.PushChannel(pkt.Variable, pkt.Value);
                        ControlPanel.UpdateChannelStats(
                            pkt.NodeId, pkt.Variable, pkt.Value, ml, pkt.Unit);

                        if (_csvRecorder.IsRecording) UpdateRecordingStatus();
                    });

                    // A line per sample is not a log. At four channels and ten hertz this wrote
                    // forty rows a second saying "received from hardware", which is the one thing
                    // the chart beside it already shows -- and it emptied the three hundred rows
                    // the silence watch, the limit alarms and the arming check deliver their
                    // answers into, in under eight seconds.
                    //
                    // What belongs here is what the simulated path has always logged and this one
                    // never did: the two edges of an anomaly. A device on a bench raising no
                    // anomaly lines at all, while the simulator raised them, is the wrong way round
                    // for the only source that matters.
                    LogAnomalyTransition(pkt, ml);

                    // Broadcast real hardware packet to WebSocket & HTML
                    var realPacket = new
                    {
                        timestamp = pkt.Timestamp.ToString("o"),
                        source = "REAL_HARDWARE",
                        port = rawPacket.PortName,
                        nodeId = pkt.NodeId,
                        variable = pkt.Variable,
                        value = pkt.Value,
                        unit = pkt.Unit,
                        anomalyScore = ml.ZScore,
                        isAnomaly = ml.IsAnomaly,
                        predicted60s = ml.PredictedValueIn60s
                    };
                    _streamingServer.BroadcastTelemetry(realPacket);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => ControlPanel.LogMessage("ERROR", $"Real serial processing error: {ex.Message}"));
        }
    }

    /// <summary>
    /// Turns a raw frame into telemetry packets: configured routing rules first, then schema
    /// inference. Shared by the hardware and simulator paths so both behave identically.
    /// </summary>
    private List<TelemetryPacket> ResolvePackets(RawPacket rawPacket)
    {
        var packets = _dataRouter.Route(rawPacket).ToList();

        // Watched here rather than further down, and deliberately before the fallback: a line no
        // rule claimed is the fact the draft most needs to report, and the positional parser would
        // turn it into Field_1 and hide it.
        _wireSurvey?.Observe(rawPacket, packets);

        return packets.Count > 0 ? packets : AutoParseRawPayload(rawPacket);
    }

    /// <summary>Ports whose payload has already been reported as unrecognised.</summary>
    private readonly HashSet<string> _unroutedPorts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Last resort for a payload no routing rule matched.
    /// </summary>
    /// <remarks>
    /// It parses numbers out of a line and gives them positional names, because that is genuinely
    /// all it knows. What it used to do was name them from a fixed list — the first number was
    /// <c>Temperature</c>, the second <c>Humidity</c>, then <c>Vibration</c>, <c>RPM</c>,
    /// <c>Voltage</c> — and stamp the port as the node.
    /// <para>
    /// That was not a cosmetic problem. A bare number carries no evidence of what it measures, so
    /// the list was asserting a quantity nobody had reported: a pressure reading was charted,
    /// alarmed on and written into the archive as a temperature, under a heading an operator has
    /// every reason to trust. This shell reached that path for every frame it received, because it
    /// registered no routing rules at all — which is why the fix is both to register them and to
    /// stop this function from claiming to know what it is looking at.
    /// </para>
    /// <para>
    /// <c>Field_1</c> is not a good channel name. It is an honest one, and it tells the operator
    /// there is a routing rule to write.
    /// </para>
    /// </remarks>
    private List<TelemetryPacket> AutoParseRawPayload(RawPacket raw)
    {
        var list = new List<TelemetryPacket>();
        if (string.IsNullOrWhiteSpace(raw.Payload)) return list;

        string payload = raw.Payload.Trim();

        // 1. JSON Auto-Parse
        if (payload.StartsWith("{") && payload.EndsWith("}"))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(payload);
                string node = raw.PortName;
                if (doc.RootElement.TryGetProperty("nodeId", out var n)) node = n.GetString() ?? node;
                else if (doc.RootElement.TryGetProperty("device", out var d)) node = d.GetString() ?? node;

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Number)
                    {
                        list.Add(new TelemetryPacket
                        {
                            NodeId = node,
                            Variable = prop.Name,
                            Value = prop.Value.GetDouble(),
                            Timestamp = raw.Timestamp
                        });
                    }
                }
                return list;
            }
            catch { }
        }

        // 2. CSV / Tab / Space separated Numbers Auto-Parse
        string[] tokens = payload.Split(new[] { ',', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);

        int varIdx = 0;
        foreach (var t in tokens)
        {
            if (double.TryParse(t, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
            {
                list.Add(new TelemetryPacket
                {
                    NodeId = raw.PortName,
                    Variable = $"Field_{varIdx + 1}",
                    Value = val,
                    Timestamp = raw.Timestamp
                });
                varIdx++;
            }
        }

        if (list.Count > 0) ReportUnroutedPayload(raw);

        return list;
    }

    /// <summary>Says once per port that its payload is being read positionally.</summary>
    /// <remarks>
    /// Once, not per packet: at telemetry rates the same sentence twenty times a second is how a
    /// log stops being read. The operator needs to know that the names on their chart came from
    /// position rather than from the device, and they need to know it while they can still act.
    /// </remarks>
    private void ReportUnroutedPayload(RawPacket raw)
    {
        if (!_unroutedPorts.Add(raw.PortName)) return;

        Dispatcher.Invoke(() => ControlPanel.LogMessage("WARN",
            $"[{raw.PortName}] 수신 데이터와 일치하는 라우팅 규칙이 없어 숫자를 순서대로 " +
            "Field_1, Field_2 … 로 표시합니다. 실제 채널 이름을 보려면 라우팅 규칙을 설정하세요."));
    }
}
