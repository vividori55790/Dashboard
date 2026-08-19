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
using System.Windows.Threading;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.Infrastructure.Serial;
using TelemetryDashboard.UI.Dialogs;
using TelemetryDashboard.UI.Docking;
using TelemetryDashboard.UI.Services;
using TelemetryDashboard.Core.Streaming;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Recording;

namespace TelemetryDashboard.UI;

public partial class MainWindow : Window
{
    private readonly ThemeService _themeService = new();
    private readonly LayoutManager _layoutManager = new();
    private readonly CommandPaletteService _commandPaletteService = new();
    private readonly DragDropHandler _dragDropHandler = new();
    private readonly LanguageService _languageService = new();
    private readonly PasswordLockService _passwordLockService = new();
    private readonly DualMcuVirtualSimulatorEngine _simulatorEngine = new();

    private readonly TelemetryMlAnalyticsEngine _mlEngine = new();
    private readonly AdaptiveSamplingController _samplingController = new();
    private readonly TelemetryStreamingServer _streamingServer = new(8080);
    private readonly TelemetryCsvRecorder _csvRecorder = new();

    // Real Hardware Ingestion Pipeline
    private readonly MultiPortSerialManager _serialManager = new();
    private readonly DataRouter _dataRouter = new();
    private CancellationTokenSource? _serialReadCts;
    private CancellationTokenSource? _simulatorReadCts;

    /// <summary>Simulation cadence: 20 Hz.</summary>
    private const double SimulationIntervalSec = 0.05;

    /// <summary>Emit a routine sample line every N ticks so the event log stays readable.</summary>
    private const int SimLogEveryNTicks = 20;

    private readonly PowerPlantSimulator _simulator = new();
    private readonly PowerTelemetryFrameBuilder _frameBuilder;

    private DispatcherTimer? _simTimer;
    private bool _isSimulating = false;
    private bool _isConnected = false;
    private int _simLogCounter;
    private string _htmlClientPath = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _frameBuilder = new PowerTelemetryFrameBuilder(_mlEngine);

        _layoutManager.AttachDockingManager(DockManager);
        CommandPalette.AttachService(_commandPaletteService);
        LockOverlay.AttachService(_passwordLockService);
        _themeService.ApplyMicaBackdrop(this);

        ResolveHtmlClientPath();
        StartStreamingServer();
        PopulatePortAndBaud();
        RegisterDefaultCommands();
        SetupSimulatorTimer();

        // Start continuous 20Hz telemetry stream so HTML gets live data immediately
        _simTimer?.Start();

        // Attach server to UI control
        StreamingControl.AttachServer(_streamingServer, _htmlClientPath);

        // Listen for HTML client commands over WebSocket
        _streamingServer.CommandReceived += (s, cmd) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (cmd.Contains("OUTAGE", StringComparison.OrdinalIgnoreCase))
                {
                    _simulator.Scenario = PowerScenario.GridOutage;
                    ControlPanel.LogMessage("SCENARIO", "⚡ [C# SIMULATOR] Grid Outage triggered! DAB UPS Battery Discharge mode active.");
                }
                else if (cmd.Contains("DAB_ANOMALY", StringComparison.OrdinalIgnoreCase))
                {
                    _simulator.Scenario = PowerScenario.DabOvercurrent;
                    ControlPanel.LogMessage("SCENARIO", "🔥 [C# SIMULATOR] DAB 배터리 과전류 주입 — Z-Score는 실측 기반으로 산출됩니다.");
                }
                else if (cmd.Contains("PSFB_ANOMALY", StringComparison.OrdinalIgnoreCase))
                {
                    _simulator.Scenario = PowerScenario.PsfbUnderVoltage;
                    ControlPanel.LogMessage("SCENARIO", "📉 [C# SIMULATOR] PSFB 48V 전압 강하 주입 — Z-Score는 실측 기반으로 산출됩니다.");
                }
                else if (cmd.Contains("NORMAL", StringComparison.OrdinalIgnoreCase))
                {
                    _simulator.Scenario = PowerScenario.Normal;
                    ControlPanel.LogMessage("SCENARIO", "✅ [C# SIMULATOR] Restored normal Grid Online & Standby mode.");
                }
            });
        };

        // Control Panel command callback: actually transmit. Previously this only wrote
        // "transmitted to serial port" into the log while nothing left the machine.
        ControlPanel.OnCommandSent += async (cmd) => await TransmitCommandAsync(cmd);
    }

    private void ResolveHtmlClientPath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates = new string[]
        {
            "power_ups_psfb_dashboard.html",
            "stream_client.html"
        };

        foreach (string c in candidates)
        {
            string[] searchPaths = new string[]
            {
                Path.Combine(baseDir, c),
                Path.Combine(baseDir, "..", c),
                Path.Combine(baseDir, "..", "..", c),
                Path.Combine(baseDir, "..", "..", "..", c),
                Path.Combine(baseDir, "..", "..", "..", "..", c),
                Path.Combine(baseDir, "..", "..", "..", "..", "..", c)
            };

            foreach (string p in searchPaths)
            {
                if (File.Exists(p))
                {
                    _htmlClientPath = Path.GetFullPath(p);
                    return;
                }
            }
        }

        _htmlClientPath = Path.Combine(baseDir, "power_ups_psfb_dashboard.html");
    }

    private void StartStreamingServer()
    {
        try
        {
            _streamingServer.Start(_htmlClientPath);
        }
        catch { }
    }

    private void PopulatePortAndBaud()
    {
        CboPort.Items.Clear();
        string[] ports = SerialPort.GetPortNames();
        if (ports.Length == 0)
        {
            ports = new string[] { "COM1", "COM3 (Virtual Dual-MCU)", "COM4 (Virtual Dual-MCU)" };
        }
        foreach (string p in ports)
        {
            CboPort.Items.Add(p);
        }
        CboPort.SelectedIndex = 0;

        CboBaudRate.Items.Clear();
        int[] bauds = new int[] { 9600, 19200, 38400, 57600, 115200, 460800, 921600 };
        foreach (int b in bauds)
        {
            CboBaudRate.Items.Add(b);
        }
        CboBaudRate.SelectedItem = 115200;
    }

    private void RegisterDefaultCommands()
    {
        _commandPaletteService.RegisterCommand("Toggle Theme", "View", () => _themeService.ToggleTheme());
        _commandPaletteService.RegisterCommand("Scope Layout", "Workspace", () => _layoutManager.ApplyPreset(LayoutPreset.ScopeMode));
        _commandPaletteService.RegisterCommand("Start Dual-MCU Simulator", "Simulation", () => StartSimulator());
        _commandPaletteService.RegisterCommand("Stop Dual-MCU Simulator", "Simulation", () => StopSimulator());
        _commandPaletteService.RegisterCommand("Open ML Analytics Modal", "AI", () => OpenMlAnalyticsModal());
    }

    private void SetupSimulatorTimer()
    {
        _simTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50) // 20 Hz telemetry stream
        };
        _simTimer.Tick += SimTimer_Tick;
    }

    /// <summary>
    /// Simulation tick: advance the physical model, score it with the production analytics
    /// engine, then publish. No value on this path is fabricated — the anomaly scores come from
    /// the same detector that scores real hardware.
    /// </summary>
    private void SimTimer_Tick(object? sender, EventArgs e)
    {
        PowerPlantState state = _simulator.Advance(SimulationIntervalSec);
        ScoredPowerFrame frame = _frameBuilder.Build(state);

        ScopeControl.PushTelemetryData(
            state.AmbientTemperature, state.AmbientHumidity, state.Vibration, state.Rpm / 100.0);

        ControlPanel.UpdateTelemetryStats(
            state.AmbientTemperature, state.AmbientHumidity, state.Vibration, state.Rpm,
            frame.Ambient, frame.Vibration);

        _streamingServer.BroadcastTelemetry(_frameBuilder.BuildAmbientFrame(state, frame.Ambient));
        _streamingServer.BroadcastTelemetry(frame.WireFrame);

        if (_csvRecorder.IsRecording)
        {
            RecordSimulationSamples(state, frame);
            UpdateRecordingStatus();
        }

        StreamingControl.UpdateMetrics();
        MaybeLogSimulationSample(state, frame);
    }

    private void RecordSimulationSamples(PowerPlantState state, ScoredPowerFrame frame)
    {
        _csvRecorder.RecordSample("COM3", "Temperature", state.AmbientTemperature,
            frame.Ambient.ZScore, frame.Ambient.IsAnomaly, frame.Ambient.PredictedValueIn60s);
        _csvRecorder.RecordSample("COM3", "Humidity", state.AmbientHumidity, 0.0, false, state.AmbientHumidity);
        _csvRecorder.RecordSample("COM3", "Vibration", state.Vibration,
            frame.Vibration.ZScore, frame.Vibration.IsAnomaly, frame.Vibration.PredictedValueIn60s);
        _csvRecorder.RecordSample("COM3", "RPM", state.Rpm, 0.0, false, state.Rpm);
        _csvRecorder.RecordSample("DAB_CONVERTER", "BatteryCurrent", state.DabBatteryCurrent,
            frame.Dab.ZScore, frame.Dab.IsAnomaly, frame.Dab.PredictedValueIn60s);
        _csvRecorder.RecordSample("PSFB_CONVERTER", "ServerVoltage", state.PsfbOutputVoltage,
            frame.Psfb.ZScore, frame.Psfb.IsAnomaly, frame.Psfb.PredictedValueIn60s);

        // Same samples into the durable archive, which is what the MATLAB export reads.
        ArchiveSimulationSamples(state);
    }

    private void UpdateRecordingStatus() =>
        StatusPacketText.Text =
            $"Rx Packets: {_csvRecorder.RecordedPacketCount:N0} | REC: {_csvRecorder.FileSizeBytes / 1024:N0} KB";

    /// <summary>Logs a periodic sample, and every genuine anomaly the detector reports.</summary>
    private void MaybeLogSimulationSample(PowerPlantState state, ScoredPowerFrame frame)
    {
        if (frame.HasAlarm)
        {
            AnomalyResult worst = frame.Dab.ZScore >= frame.Psfb.ZScore ? frame.Dab : frame.Psfb;
            ControlPanel.LogTelemetryEvent(
                worst == frame.Dab ? "DAB_CONVERTER" : "PSFB_CONVERTER",
                worst == frame.Dab ? "BatteryCurrent" : "ServerVoltage",
                worst.CurrentValue, worst.ZScore,
                $"{frame.Severity} anomaly detected by Z-score analysis");
            return;
        }

        if (++_simLogCounter % SimLogEveryNTicks != 0) return;

        ControlPanel.LogTelemetryEvent("COM3", "Temperature",
            state.AmbientTemperature, frame.Ambient.ZScore,
            $"TEMP={state.AmbientTemperature:F2}C HUM={state.AmbientHumidity:F1}% " +
            $"VIB={state.Vibration:F3}g RPM={state.Rpm:F0}");
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _streamingServer.Stop();
        _simulatorEngine.StopSimulation();
        ShutdownArchive();
    }
}