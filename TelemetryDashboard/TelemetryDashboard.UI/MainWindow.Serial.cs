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
            string portName = CboPort.SelectedItem?.ToString() ?? "COM1";
            if (portName.Contains(" ")) portName = portName.Split(' ')[0]; // Strip suffix e.g. "(Virtual Dual-MCU)"

            int baudRate = 115200;
            if (CboBaudRate.SelectedItem is int b) baudRate = b;
            else if (int.TryParse(CboBaudRate.SelectedItem?.ToString(), out int bParsed)) baudRate = bParsed;

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
                bool ok = await _serialManager.ConnectPortAsync(portName, baudRate);
                if (ok)
                {
                    _isConnected = true;
                    BtnToggleConnect.Content = "❌ 연결 해제";
                    BtnToggleConnect.Background = new SolidColorBrush(Color.FromRgb(255, 85, 85));
                    StatusConnectionText.Text = "Status: Connected (REAL HARDWARE)";
                    StatusConnectionText.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                    StatusPortText.Text = $"Port: {portName} | Baud: {baudRate}";
                    ControlPanel.LogMessage("SYSTEM", $"✅ Connected to real hardware port {portName} @ {baudRate} Baud.");

                    _serialReadCts = new CancellationTokenSource();
                    _ = Task.Run(() => ProcessRealSerialPacketsAsync(_serialReadCts.Token));
                }
                else
                {
                    ControlPanel.LogMessage("ERROR", $"❌ Failed to open serial port {portName}. Check if device is plugged in.");
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
            _serialReadCts?.Cancel();
            await _serialManager.DisconnectAllAsync();

            BtnToggleConnect.Content = "🔗 연결 시작";
            BtnToggleConnect.Background = new SolidColorBrush(Color.FromRgb(122, 162, 247));
            StatusConnectionText.Text = "Status: Disconnected";
            StatusConnectionText.Foreground = new SolidColorBrush(Color.FromRgb(255, 85, 85));
            ControlPanel.LogMessage("SYSTEM", "Disconnected from serial port.");
        }
    }

    private async Task ProcessRealSerialPacketsAsync(CancellationToken token)
    {
        try
        {
            await foreach (var rawPacket in _serialManager.PacketReader.ReadAllAsync(token))
            {
                List<TelemetryPacket> packets = ResolvePackets(rawPacket);

                foreach (var pkt in packets)
                {
                    var ml = _mlEngine.AnalyzeChannel($"{pkt.NodeId}.{pkt.Variable}", pkt.Value);

                    Dispatcher.Invoke(() =>
                    {
                        // Plot only the channel that actually arrived. The previous code padded the
                        // scope and the statistics with literals (50.0 humidity, 0.1 vibration,
                        // 1200 rpm) whenever a temperature packet came in, so the operator saw
                        // invented readings for sensors the device had never reported.
                        ScopeControl.PushChannel(pkt.Variable, pkt.Value);
                        ControlPanel.UpdateChannelStats(pkt.NodeId, pkt.Variable, pkt.Value, ml);

                        if (_csvRecorder.IsRecording)
                        {
                            _csvRecorder.RecordSample(pkt.NodeId, pkt.Variable, pkt.Value, ml.ZScore, ml.IsAnomaly, ml.PredictedValueIn60s);
                            UpdateRecordingStatus();
                        }

                        ControlPanel.LogTelemetryEvent(pkt.NodeId, pkt.Variable, pkt.Value, ml.ZScore,
                            $"{pkt.Value:F2}{pkt.Unit} received from hardware");
                    });

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
        return packets.Count > 0 ? packets : AutoParseRawPayload(rawPacket);
    }

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
        string[] defaultVarNames = new[] { "Temperature", "Humidity", "Vibration", "RPM", "Voltage", "Current", "Power" };

        int varIdx = 0;
        foreach (var t in tokens)
        {
            if (double.TryParse(t, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
            {
                string varName = varIdx < defaultVarNames.Length ? defaultVarNames[varIdx] : $"Channel_{varIdx + 1}";
                list.Add(new TelemetryPacket
                {
                    NodeId = raw.PortName,
                    Variable = varName,
                    Value = val,
                    Timestamp = raw.Timestamp
                });
                varIdx++;
            }
        }

        return list;
    }
}
