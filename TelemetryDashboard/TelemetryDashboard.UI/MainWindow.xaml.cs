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
    /// <summary>
    /// The synthetic source, rebuilt whenever the operator selects a different profile.
    /// </summary>
    /// <remarks>
    /// Not readonly and not created here, because what it generates depends on a choice made after
    /// construction. The engine it replaces was fixed at two nodes and four channels named for one
    /// customer's hardware, so selecting another profile changed the sliders and the captions while
    /// the stream underneath stayed theirs.
    /// </remarks>
    private ProfileSimulatorEngine? _simulatorEngine;

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

    private readonly PowerPlantSimulator _simulator = new();
    private readonly PowerTelemetryFrameBuilder _frameBuilder;

    private DispatcherTimer? _simTimer;
    private bool _isSimulating = false;
    private bool _isConnected = false;
    private string _htmlClientPath = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _frameBuilder = new PowerTelemetryFrameBuilder(_mlEngine);

        _layoutManager.AttachDockingManager(DockManager);
        CommandPalette.AttachService(_commandPaletteService);
        LockOverlay.AttachService(_passwordLockService);
        // The theme the operator chose last time, applied before anything is shown. Nothing read
        // the stored choice back before, so the app opened dark however it had been left.
        _themeService.ApplyStoredTheme();
        _themeService.ApplyMicaBackdrop(this);

        ResolveHtmlClientPath();
        RegisterDefaultRoutingRules();
        StartStreamingServer();
        PopulatePortAndBaud();
        RegisterDefaultCommands();

        // The palette lists the ribbon's captions, so it has to be rebuilt when they change
        // language -- otherwise Ctrl+Shift+P offers Korean names for buttons now reading English.
        _languageService.LanguageChanged += (_, _) => RegisterDefaultCommands();
        SetupAlerts();

        // The twin says out loud which mesh it settled on and why. Routed to the event log as well
        // as to its own toolbar, because that decision is taken at start-up on a panel that may
        // not be the one in front of the operator.
        TwinControl.Notice += message => ControlPanel.LogMessage("TWIN", message);

        // Watching the clock as well as the values. Until now the shell could not see a channel
        // that had simply stopped: the scope holds the last point, the statistics hold the last
        // mean, and the z-score sits at zero because the distribution stopped moving too.
        ControlPanel.StartSilenceWatch();
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
    /// Teaches the router this repository's own frame format.
    /// </summary>
    /// <remarks>
    /// The shell constructed a router and registered nothing in it, so <c>Route</c> returned no
    /// packets for every line it was ever given and each one fell through to the raw fallback —
    /// which read the numbers positionally and named the first one <c>Temperature</c>. Both halves
    /// of that were wrong and they hid each other: the router looked fine because something always
    /// came out the other end, and the fallback looked fine because it always produced plausible
    /// names. The same rules the console host uses are registered here, from the one definition.
    /// </remarks>
    private void RegisterDefaultRoutingRules()
    {
        foreach (RoutingRule rule in DefaultRoutingRules.Create())
        {
            _dataRouter.RegisterRule(rule);
        }
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

    /// <summary>
    /// Fills the palette: everything on the ribbon, then the few commands the ribbon has no button
    /// for.
    /// </summary>
    /// <remarks>
    /// The ribbon first, so a hand-written entry of the same name wins — registration is by name
    /// and the later call replaces the earlier. The palette used to hold these five alone while the
    /// ribbon carried some forty, which made it a shortcut to the handful of commands an operator
    /// was least likely to be hunting for.
    /// </remarks>
    private void RegisterDefaultCommands()
    {
        // Cleared and re-read, because this also runs when the language changes: the ribbon's
        // captions are the palette's command names, and registration is by name.
        _commandPaletteService.ClearCommands();

        foreach (CommandItem command in RibbonCommandHarvest.From(Ribbon))
        {
            _commandPaletteService.RegisterCommand(command.Name, command.Category, command.Action);
        }

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

        // The scope is not fed here. It is fed by the ingest consumer, which is the path a real
        // device takes; feeding it from this tick as well put every quantity on the chart twice,
        // under two names, from two different generators. See MainWindow.Simulation.cs.
        ControlPanel.UpdateTelemetryStats(
            state.AmbientTemperature, state.AmbientHumidity, state.Vibration, state.Rpm,
            frame.Ambient, frame.Vibration);

        // Nothing is broadcast from this tick any more, and measuring is what settled it. These
        // two lines put forty frames a second on the wire in the shape this product used before it
        // had one -- a flat {temp, humidity, rpm} and a nested {grid, dab, psfb, alarm} -- while
        // every page it ships reads {nodeId, variable, value, unit} and discards anything else.
        // Counted on the running shell with a browser attached and the simulator not started: 214
        // messages received, 0 readable. The comment above the builder said the bundled consoles
        // bind to these names; none of them has for some time.
        //
        // The model still runs, because the ambient readouts and the twin's thermal field are read
        // off it above. What it no longer does is publish.

        // The twin's thermal field. The node ids are the profile's, not this file's: the simulator
        // knows a DAB temperature and a PSFB temperature, and the profile in force is what says
        // which port each of those boards is and where it sits. A shell that named the coordinates
        // here would be drawing one customer's rig for everybody, which is the mistake ProfileNode
        // exists to have already fixed once.
        TwinControl.UpdateThermal(_activeProfile, new Dictionary<string, double>
        {
            ["COM3"] = state.DabTemperature,
            ["COM4"] = state.PsfbTemperature
        });

        if (_csvRecorder.IsRecording) UpdateRecordingStatus();

        StreamingControl.UpdateMetrics();
    }

    private void UpdateRecordingStatus() =>
        StatusRecordingText.Text =
            $"녹화 중 · {_csvRecorder.RecordedPacketCount:N0}건 · {_csvRecorder.FileSizeBytes / 1024:N0} KB";

    // Removed: MaybeLogSimulationSample. It logged an alarm as coming from DAB_CONVERTER or
    // PSFB_CONVERTER whichever profile was selected, and a heartbeat line reading
    // "TEMP=.. HUM=.. VIB=.. RPM=.." that described the demo's four quantities and nothing else.
    // Both are now written by the ingest consumer, which knows the channel a reading actually came
    // from. The heartbeat is gone rather than generalised: a line every second saying that nothing
    // is wrong is what teaches an operator to stop reading the log.

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _streamingServer.Stop();
        _simulatorEngine?.StopSimulation();
        ShutdownArchive();
    }
}