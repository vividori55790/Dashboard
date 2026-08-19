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
using TelemetryDashboard.Core.Analytics;

namespace TelemetryDashboard.UI;

/// <summary>Simulation controls: grid scenarios, converter setpoints, and fault injection.</summary>
public partial class MainWindow
{
    private void BtnSetGridNormal_Click(object sender, RoutedEventArgs e)
    {
        _simulator.Scenario = PowerScenario.Normal;
        _simulator.GridVoltageSetpoint = 380.0;
        ControlPanel.LogMessage("CONTROL", "⚡ [C# 제어] 상용 전력망 정상 모드 (380V 급전)");
    }

    private void BtnSetGridOutage_Click(object sender, RoutedEventArgs e)
    {
        _simulator.Scenario = PowerScenario.GridOutage;
        _simulator.GridVoltageSetpoint = 0.0;
        ControlPanel.LogMessage("CONTROL", "🚨 [C# 제어] 전력망 정전/차단 ➔ UPS 배터리 비상 방전 모드 전환");
    }

    private void SliderDabBus_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _simulator.DabBusVoltageSetpoint = e.NewValue;
        if (TxtDabBus != null) TxtDabBus.Text = $"{e.NewValue:F0}V";
    }

    private void SliderPsfbVolt_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _simulator.PsfbVoltageSetpoint = e.NewValue;
        if (TxtPsfbVolt != null) TxtPsfbVolt.Text = $"{e.NewValue:F1}V";
    }

    private void SliderServerLoad_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _simulator.ServerLoadSetpoint = e.NewValue;
        if (TxtServerLoad != null) TxtServerLoad.Text = $"{e.NewValue:F0}%";
    }

    private void BtnInjectDabAnomaly_Click(object sender, RoutedEventArgs e)
    {
        _simulator.Scenario = PowerScenario.DabOvercurrent;
        ControlPanel.LogMessage("CONTROL", "🔥 [C# 제어] DAB 배터리 과전류 주입 — Z-Score는 실측값으로 산출됩니다");
    }

    private void BtnInjectPsfbAnomaly_Click(object sender, RoutedEventArgs e)
    {
        _simulator.Scenario = PowerScenario.PsfbUnderVoltage;
        ControlPanel.LogMessage("CONTROL", "📉 [C# 제어] PSFB 48V 전압 강하 주입 — Z-Score는 실측값으로 산출됩니다");
    }

    private void BtnResetAnomaly_Click(object sender, RoutedEventArgs e)
    {
        _simulator.Reset();
        if (SliderDabBus != null) SliderDabBus.Value = 400;
        if (SliderPsfbVolt != null) SliderPsfbVolt.Value = 48;
        if (SliderServerLoad != null) SliderServerLoad.Value = 82;
        ControlPanel.LogMessage("CONTROL", "✅ [C# 제어] 모든 전력 수치 및 경보 정상 복구");
    }

    private void BtnToggleSimulator_Click(object sender, RoutedEventArgs e)
    {
        if (_isSimulating)
        {
            StopSimulator();
        }
        else
        {
            StartSimulator();
        }
    }

    private void StartSimulator()
    {
        if (_isConnected)
        {
            ControlPanel.LogMessage("SIMULATOR", "Disconnect the hardware port before starting the simulator.");
            return;
        }

        _isSimulating = true;
        _simTimer?.Start();

        // Consume the virtual MCU stream through the same parser -> router -> ML path the real
        // hardware uses. Previously the engine was started and its packets thrown away, so the
        // simulator exercised none of the ingest pipeline it exists to exercise.
        _simulatorEngine.StartSimulation();
        _simulatorReadCts = new CancellationTokenSource();
        _ = Task.Run(() => ConsumeSimulatedPacketsAsync(_simulatorReadCts.Token));

        ControlPanel.LogMessage("SIMULATOR", "Dual-MCU Virtual Simulator started (COM3/COM4 ingest active).");
    }

    private void StopSimulator()
    {
        _isSimulating = false;
        _simTimer?.Stop(); // the tick timer kept running and kept broadcasting simulated frames

        _simulatorReadCts?.Cancel();
        _simulatorReadCts = null;
        _simulatorEngine.StopSimulation();

        ControlPanel.LogMessage("SIMULATOR", "Dual-MCU Virtual Simulator stopped.");
    }

    /// <summary>
    /// Routes virtual-MCU packets through the production ingest path so parsing, routing and
    /// anomaly scoring are all genuinely exercised in simulation.
    /// </summary>
    private async Task ConsumeSimulatedPacketsAsync(CancellationToken token)
    {
        try
        {
            await foreach (RawPacket raw in _simulatorEngine.StreamSimulatedPackets(token))
            {
                foreach (TelemetryPacket packet in ResolvePackets(raw))
                {
                    AnomalyResult analysis = _mlEngine.AnalyzeChannel(
                        $"{packet.NodeId}.{packet.Variable}", packet.Value);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        ScopeControl.PushChannel(packet.Variable, packet.Value);
                        ControlPanel.UpdateChannelStats(packet.NodeId, packet.Variable, packet.Value, analysis);
                    });

                    _streamingServer.BroadcastTelemetry(new
                    {
                        timestamp = packet.Timestamp.ToString("o"),
                        source = "VIRTUAL_MCU",
                        nodeId = packet.NodeId,
                        variable = packet.Variable,
                        value = packet.Value,
                        unit = packet.Unit,
                        anomalyScore = analysis.ZScore,
                        isAnomaly = analysis.IsAnomaly
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Simulator stopped.
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
                ControlPanel.LogMessage("ERROR", $"Simulated ingest failed: {ex.Message}"));
        }
    }
}
