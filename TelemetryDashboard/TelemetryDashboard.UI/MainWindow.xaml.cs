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

        // Before the first tick, because the tick asks the active profile which channels to plot.
        // This call was missing entirely, which is why the profile picker opened empty and the
        // subtitle kept its placeholder: the collection behind the picker was never filled.
        InitializeProfiles();

        // Start continuous 20Hz telemetry stream so HTML gets live data immediately
        _simTimer?.Start();

        // Attach server to UI control
        StreamingControl.AttachServer(_streamingServer, _htmlClientPath);

        // Listen for HTML client commands over WebSocket
        _streamingServer.CommandReceived += (s, cmd) => Dispatcher.Invoke(() => ApplyWebConsoleCommand(cmd));

        // Control Panel command callback: actually transmit. Previously this only wrote
        // "transmitted to serial port" into the log while nothing left the machine.
        ControlPanel.OnCommandSent += async (cmd) => await TransmitCommandAsync(cmd);
    }

    /// <summary>
    /// Puts the built-in model into the fault a browser client asked for.
    /// </summary>
    /// <remarks>
    /// The command tokens belong to the bundled example page's wire protocol, so they are matched
    /// as they are. What the log says about them is a different matter: it used to narrate one
    /// customer's converter — "DAB battery overcurrent injected", "PSFB 48 V rail sagging" — on
    /// every installation, whichever system the operator had selected. The line now reports the
    /// command that arrived and the fault the model was actually put into, which is true of every
    /// profile because it describes the model rather than anybody's hardware.
    /// <para>
    /// No emoji in a log line. The event log renders these through the panel's text styles, and an
    /// emoji comes from a colour font that ignores Foreground entirely — so the one character in
    /// the row that the palette cannot reach was the one drawing most of the attention.
    /// </para>
    /// </remarks>
    private void ApplyWebConsoleCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        PowerScenario? fault = null;

        if (command.Contains("OUTAGE", StringComparison.OrdinalIgnoreCase))
        {
            fault = PowerScenario.GridOutage;
        }
        else if (command.Contains("DAB_ANOMALY", StringComparison.OrdinalIgnoreCase))
        {
            fault = PowerScenario.DabOvercurrent;
        }
        else if (command.Contains("PSFB_ANOMALY", StringComparison.OrdinalIgnoreCase))
        {
            fault = PowerScenario.PsfbUnderVoltage;
        }
        else if (command.Contains("NORMAL", StringComparison.OrdinalIgnoreCase))
        {
            fault = PowerScenario.Normal;
        }

        // Silently dropping a command the operator watched themselves press is worse than saying
        // it was not understood; the browser gets no acknowledgement either way.
        if (fault is null)
        {
            ControlPanel.LogMessage("WARN", $"웹 콘솔 명령 '{command}' 을 알 수 없어 적용하지 않았습니다.");
            return;
        }

        _simulator.Scenario = fault.Value;
        ControlPanel.LogMessage("SCENARIO",
            $"웹 콘솔 명령 '{command}' — 시뮬레이터 고장 모델을 '{fault.Value}' 로 설정했습니다. " +
            "이상 점수는 검출기가 계산합니다.");
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

        // Only ports this machine reports. The fallback here used to add "COM1" and two entries
        // named "COM3 (Virtual Dual-MCU)" and "COM4 (Virtual Dual-MCU)" when there were none, so a
        // bench with nothing plugged in presented three selectable targets, two of them under names
        // the serial stack would not even accept. The same fabrication was removed from the
        // firmware dialog for the same reason; this was the copy of it that stayed.
        string[] ports = SerialPort.GetPortNames();
        foreach (string p in ports)
        {
            CboPort.Items.Add(p);
        }

        CboPort.IsEnabled = ports.Length > 0;
        if (ports.Length == 0)
        {
            CboPort.Items.Add("사용 가능한 직렬 포트 없음");
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

        PushProfileChannelsToScope(state);

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
        StatusRecordingText.Text =
            $"녹화 중 · {_csvRecorder.RecordedPacketCount:N0}건 · {_csvRecorder.FileSizeBytes / 1024:N0} KB";

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