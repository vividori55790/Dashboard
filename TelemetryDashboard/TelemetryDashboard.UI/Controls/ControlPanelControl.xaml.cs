using System;
using System.Globalization;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.UI.Controls;

/// <summary>One row in the operator's event log.</summary>
/// <remarks>
/// Every default is deliberately blank or a dash. The previous defaults — node "COM3", variable
/// "Temp", value "0.0", z-score "0.0σ" — described a specific, plausible, calm measurement that
/// no sensor ever reported, so a row that failed to populate looked exactly like a healthy one.
/// </remarks>
public class EventLogEntry
{
    /// <summary>Rendered when a field carries no value; never a number that could be mistaken for data.</summary>
    public const string NoValue = "-";

    public string Time { get; set; } = string.Empty;
    public string Level { get; set; } = "INFO"; // INFO, WARN, CRIT
    public string Node { get; set; } = NoValue;
    public string Variable { get; set; } = NoValue;
    public string Value { get; set; } = NoValue;
    public string ZScore { get; set; } = NoValue;
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Value and z-score as one right-aligned figure for the log row, or nothing at all when the
    /// entry carries no measurement.
    /// </summary>
    /// <remarks>
    /// Rendering "- -" on every plain log line would fill the column with placeholders; an entry
    /// that measured nothing shows nothing, and only a real reading occupies the space.
    /// </remarks>
    public string Reading =>
        Value == NoValue && ZScore == NoValue ? string.Empty : $"{Value}  {ZScore}";

    /// <summary>The row as one sentence, for anything that reads it rather than draws it.</summary>
    /// <remarks>
    /// The list renders these through a template, so the columns on screen were always right — and
    /// everything that stringifies a row without one got
    /// "TelemetryDashboard.UI.Controls.EventLogEntry", including the name a screen reader announces
    /// for every line of the event log. The same defect was fixed on
    /// <see cref="Core.Simulator.MonitoringProfile"/> for the same reason.
    /// </remarks>
    public override string ToString()
    {
        string reading = Reading;
        string where = Node == NoValue ? Variable : $"{Node} {Variable}";

        return string.IsNullOrEmpty(reading)
            ? $"{Time} {Level} {where}: {Message}"
            : $"{Time} {Level} {where}: {Message} ({reading.Trim()})";
    }
}

/// <summary>
/// One generated node switch: the device the profile named, and what has been commanded of it.
/// </summary>
/// <remarks>
/// The caption reports the last command this panel sent, and says so in as many words until one has
/// been sent. The buttons it replaces opened captioned "On" with nothing having asked the device
/// anything, which is a reading rather than a control — and a reading nobody took.
/// <para>
/// The command is sent from the state setter rather than from a click handler, and that is not a
/// stylistic choice. A toggle button can be flipped by a click, by the space bar, and by assistive
/// technology through the toggle pattern — and the last of those changes the checked state without
/// ever raising Click. Hanging the command off the click left exactly one route that moved the
/// switch on screen while nothing left the machine, which is the failure the caption exists to make
/// impossible. Driving it from the state means every route agrees.
/// </para>
/// </remarks>
public sealed class NodePowerToggle : INotifyPropertyChanged
{
    /// <summary>Caption state before this panel has commanded the device anything.</summary>
    private const string NotCommanded = "no power command sent";

    private readonly Action<NodePowerToggle, bool> _onCommanded;
    private bool _isOn;
    private bool _commanded;

    public NodePowerToggle(ProfileNode node, Action<NodePowerToggle, bool> onCommanded)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(onCommanded);

        _onCommanded = onCommanded;
        Id = node.Id;
        Label = node.Label;

        // Falls back to the id rather than to nothing: the id is what leaves the machine in the
        // command, so it is worth being able to read even when the profile wrote no description.
        Description = string.IsNullOrWhiteSpace(node.Description) ? node.Id : node.Description;
    }

    /// <summary>The key sent in the power command, straight from the profile.</summary>
    public string Id { get; }

    public string Label { get; }

    public string Description { get; }

    /// <summary>The state this panel has commanded. Two-way bound to the switch.</summary>
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value && _commanded) return;

            _isOn = value;
            _commanded = true;
            Raise(nameof(IsOn));
            Raise(nameof(Caption));
            _onCommanded(this, value);
        }
    }

    public string Caption =>
        _commanded ? $"{Label} — {(_isOn ? "on" : "off")}" : $"{Label} — {NotCommanded}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

public partial class ControlPanelControl : UserControl
{
    public event Action<string>? OnCommandSent;

    private readonly ObservableCollection<EventLogEntry> _eventLogEntries = new();
    private readonly ObservableCollection<NodePowerToggle> _nodeToggles = new();
    private readonly List<double> _sparklineBuffer = new();

    /// <summary>Nominal serialised packet size used for the throughput estimate.</summary>
    private const int EstimatedPacketBytes = 32;

    private DateTime _rateWindowStart = DateTime.Now;
    private long _bytesSinceRateReset;
    private int _totalPackets = 0;
    private double _tempMin = double.MaxValue;
    private double _tempMax = double.MinValue;
    private double _vibMax = double.MinValue;

    public ControlPanelControl()
    {
        InitializeComponent();
        DgEventLog.ItemsSource = _eventLogEntries;
        NodeControlList.ItemsSource = _nodeToggles;
        NodeControlList.Visibility = Visibility.Collapsed;
        LogMessage("SYSTEM", "Control panel ready.");
    }

    /// <summary>
    /// Rebuilds the node switches from the profile the host has just applied.
    /// </summary>
    /// <remarks>
    /// A profile with no nodes leaves the list empty and puts a sentence in its place. The two are
    /// deliberately different on screen: a panel that shows nothing looks the same whether the
    /// profile declared nothing or the panel failed to render, and only one of those is fine.
    /// </remarks>
    public void ShowProfileNodes(string profileName, IReadOnlyList<ProfileNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        Dispatcher.Invoke(() =>
        {
            _nodeToggles.Clear();
            foreach (ProfileNode node in nodes)
            {
                _nodeToggles.Add(new NodePowerToggle(node, SendNodePowerCommand));
            }

            bool any = _nodeToggles.Count > 0;
            NodeControlList.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            TxtNoNodes.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            TxtNoNodes.Text =
                $"'{profileName}' declares no nodes, so there is nothing here to switch. " +
                "Add a nodes list to the profile to control devices from this panel.";
        });
    }

    /// <summary>Resolves a theme brush, or null outside a running application.</summary>
    private static Brush? ThemeBrush(FrameworkElement scope, string key) =>
        scope.TryFindResource(key) as Brush;

    public void LogMessage(string tag, string message)
    {
        Dispatcher.Invoke(() =>
        {
            string timeStr = DateTime.Now.ToString("HH:mm:ss.fff");
            string level = "INFO";
            if (tag.Contains("ERR") || tag.Contains("CRIT") || message.Contains("CRITICAL")) level = "CRIT";
            else if (tag.Contains("WARN") || tag.Contains("SIM")) level = "WARN";

            // Only a message that names a port gets a port in the column. This defaulted to "COM3",
            // so every panel and system line in the log was attributed to a port that had nothing
            // to do with it — and on a machine with no COM3 at all, to a port that does not exist.
            string node = EventLogEntry.NoValue;
            if (message.Contains("[COM4]")) node = "COM4";
            else if (message.Contains("[COM3]")) node = "COM3";

            var entry = new EventLogEntry
            {
                Time = timeStr,
                Level = level,
                Node = node,
                Variable = tag,
                Value = "-",
                ZScore = "-",   // this path carries no measurement, so it reports no sigma
                Message = message
            };

            _eventLogEntries.Add(entry);
            if (_eventLogEntries.Count > 300)
            {
                _eventLogEntries.RemoveAt(0);
            }

            if (ChkAutoScroll.IsChecked == true && _eventLogEntries.Count > 0)
            {
                DgEventLog.ScrollIntoView(_eventLogEntries[^1]);
            }
        });
    }

    /// <summary>Raised when the analytics engine returns an anomaly verdict for a channel.</summary>
    /// <remarks>
    /// An event rather than a call into the window, so this control keeps not knowing what the
    /// application does about an alarm. What it does today is show a banner and speak; before this
    /// it wrote a line into the log beneath the operator's attention, and on the hardware path it
    /// did not even do that.
    /// </remarks>
    public event Action<string, bool>? AlertRaised;

    /// <summary>
    /// Decides whether a scored reading is an alarm, and tells anyone listening.
    /// </summary>
    /// <remarks>
    /// The engine's own verdict is used rather than a threshold repeated here. A second copy of
    /// the rule is a second place for it to drift, and this file already carried one: the
    /// simulated path compared the z-score against a literal while the hardware path checked
    /// nothing at all.
    /// </remarks>
    private readonly AnomalyAlarmGate _alarmGate = new();

    private void RaiseIfAnomalous(string node, string variable, double value, AnomalyResult analysis)
    {
        string channel = $"{node}.{variable}";
        AlarmTransition transition = _alarmGate.Evaluate(channel, analysis);

        // Only the transition into alarm interrupts. The first version of this raised on the
        // engine's own IsAnomaly, which is a 2.5 sigma detection bar meant for marking a chart --
        // measured on a running window, that put a banner up on an idle machine within seconds and
        // then replaced it on every sample. An alarm that is always on is an alarm nobody reads.
        if (transition == AlarmTransition.Entered)
        {
            string message = string.Create(CultureInfo.InvariantCulture,
                $"{channel} = {value:G6} ({analysis.ZScore:F1} sigma)");

            LogTelemetryEvent(node, variable, value, analysis.ZScore, "Anomaly: " + message);
            AlertRaised?.Invoke(message, true);
        }
        else if (transition == AlarmTransition.Cleared)
        {
            // Logged rather than raised. The operator needs to know it ended, and a banner for
            // "nothing is wrong any more" is an interruption that carries no action.
            LogTelemetryEvent(node, variable, value, analysis.ZScore, $"Recovered: {channel}");
        }
    }

    public void LogTelemetryEvent(string node, string variable, double value, double zScore, string message)
    {
        Dispatcher.Invoke(() =>
        {
            string timeStr = DateTime.Now.ToString("HH:mm:ss.fff");
            string level = "INFO";
            if (zScore >= 3.5 || message.Contains("CRIT")) level = "CRIT";
            else if (zScore >= 2.0 || message.Contains("WARN")) level = "WARN";

            var entry = new EventLogEntry
            {
                Time = timeStr,
                Level = level,
                Node = node,
                Variable = variable,
                Value = value.ToString("F2"),
                ZScore = $"{zScore:F1}σ",
                Message = message
            };

            _eventLogEntries.Add(entry);
            if (_eventLogEntries.Count > 300)
            {
                _eventLogEntries.RemoveAt(0);
            }

            if (ChkAutoScroll.IsChecked == true && _eventLogEntries.Count > 0)
            {
                DgEventLog.ScrollIntoView(_eventLogEntries[^1]);
            }
        });
    }

    /// <summary>
    /// Updates the live statistics panel from analytics results computed by the shared engine.
    /// </summary>
    /// <remarks>
    /// This panel used to recompute its own mean and standard deviation, so the sigma shown here
    /// disagreed with the sigma driving alerts and the DVR. It also derived the vibration figure
    /// as <c>vib / 0.5</c> — a ratio, not a z-score — and then labelled it <c>[NORMAL]</c>
    /// unconditionally, so a vibration excursion could never turn that indicator red.
    /// </remarks>
    public void UpdateTelemetryStats(
        double temp, double hum, double vib, double rpm,
        AnomalyResult temperature, AnomalyResult vibration)
    {
        Dispatcher.Invoke(() =>
        {
            RegisterPacket();

            if (temp < _tempMin) _tempMin = temp;
            if (temp > _tempMax) _tempMax = temp;
            if (vib > _vibMax) _vibMax = vib;

            TxtTempMinMax.Text = $"{_tempMin:F1} / {_tempMax:F1} °C";
            TxtVibMax.Text = $"{_vibMax:F2} g";
            TxtAnomalyStats.Text =
                $"Stats: μ = {temperature.Mean:F1} °C | σ = {temperature.StdDev:F2} | n = {temperature.SampleCount}";

            ApplyZScore(TxtTempZScore, temperature);
            ApplyZScore(TxtVibZScore, vibration);

            RaiseIfAnomalous("COM3", "Temp", temp, temperature);
            RaiseIfAnomalous("COM3", "Vib", vib, vibration);

            _sparklineBuffer.Add(temp);
            if (_sparklineBuffer.Count > 50) _sparklineBuffer.RemoveAt(0);
            DrawSparkline();
        });
    }

    /// <summary>Updates the panel for a single channel arriving from real hardware.</summary>
    /// <param name="unit">
    /// What the reading is in, carried because a declared limit is written in a unit and refuses to
    /// judge a channel reporting another. Dropping it here made every band unjudgeable.
    /// </param>
    public void UpdateChannelStats(
        string node, string variable, double value, AnomalyResult analysis, string? unit = null)
    {
        Dispatcher.Invoke(() =>
        {
            RegisterPacket();

            bool isTemperature = variable.StartsWith("Temp", StringComparison.OrdinalIgnoreCase);
            bool isVibration = variable.StartsWith("Vib", StringComparison.OrdinalIgnoreCase);

            if (isTemperature)
            {
                if (value < _tempMin) _tempMin = value;
                if (value > _tempMax) _tempMax = value;
                TxtTempMinMax.Text = $"{_tempMin:F1} / {_tempMax:F1} °C";
                ApplyZScore(TxtTempZScore, analysis);

                _sparklineBuffer.Add(value);
                if (_sparklineBuffer.Count > 50) _sparklineBuffer.RemoveAt(0);
                DrawSparkline();
            }
            else if (isVibration)
            {
                if (value > _vibMax) _vibMax = value;
                TxtVibMax.Text = $"{_vibMax:F2} g";
                ApplyZScore(TxtVibZScore, analysis);
            }

            TxtAnomalyStats.Text =
                $"Stats [{node}.{variable}]: μ = {analysis.Mean:F2} | σ = {analysis.StdDev:F2} | n = {analysis.SampleCount}";

            // This is the path a real device takes, and it scored every reading and then discarded
            // the verdict: an anomaly on hardware produced no alarm, no banner and not even a log
            // line. Only the simulated path checked, which is the wrong way round for a defect to
            // be arranged -- the demo warned and the plant did not.
            RaiseIfAnomalous(node, variable, value, analysis);

            // A different question from the one above, and the one with an action attached:
            // "unusual lately" is about a distribution, "outside the band somebody agreed" is
            // about the machine. The profile states the bands and nothing here consulted them.
            RaiseIfOutsideLimits(node, variable, value, unit);

            // The other half of the same question. RaiseIfAnomalous asks whether this reading is
            // unusual; this asks whether readings are still arriving at all, which no verdict
            // about a value can answer.
            ObserveForSilence($"{node}.{variable}", DateTime.UtcNow);
        });
    }

    /// <summary>Counts a packet and refreshes the throughput readouts.</summary>
    private void RegisterPacket()
    {
        _totalPackets++;
        _bytesSinceRateReset += EstimatedPacketBytes;

        // Units live in the tile caption, so the value stays a number and the column stays narrow.
        TxtPacketCount.Text = $"{_totalPackets:N0}";

        // Throughput over the elapsed interval. The old readout divided the cumulative packet
        // count by 1024 and labelled it KB/s, so the "rate" only ever climbed.
        double elapsed = (DateTime.Now - _rateWindowStart).TotalSeconds;
        if (elapsed >= 1.0)
        {
            TxtDataRate.Text = $"{_bytesSinceRateReset / 1024.0 / elapsed:F1}";
            _bytesSinceRateReset = 0;
            _rateWindowStart = DateTime.Now;
        }
    }

    /// <summary>
    /// Renders an analysis result with the severity banding the alert pipeline uses, or a dash
    /// while the channel is still warming up.
    /// </summary>
    /// <remarks>
    /// A result with no verdict carries <c>ZScore == 0</c>, which the banding below would paint
    /// green as "0.0σ [NORMAL]". During the first samples after a connect that is the panel
    /// asserting a normality nothing has established yet.
    /// </remarks>
    private static void ApplyZScore(TextBlock target, AnomalyResult analysis)
    {
        if (!analysis.HasVerdict)
        {
            target.Text = $"—  no baseline yet ({analysis.SampleCount} samples)";
            Recolour(target, "TextTertiaryBrush");
            return;
        }

        ApplyZScore(target, analysis.ZScore);
    }

    /// <summary>Renders a z-score with the severity banding the alert pipeline uses.</summary>
    private static void ApplyZScore(TextBlock target, double zScore)
    {
        if (zScore >= 3.5)
        {
            target.Text = $"{zScore:F1}σ  Critical";
            Recolour(target, "DangerBrush");
        }
        else if (zScore >= 2.0)
        {
            target.Text = $"{zScore:F1}σ  Warning";
            Recolour(target, "WarningBrush");
        }
        else
        {
            target.Text = $"{zScore:F1}σ  Normal";
            Recolour(target, "SuccessBrush");
        }
    }

    /// <summary>
    /// Paints a reading with a status token. Colour here reports a verdict the analytics engine
    /// produced, which is the only thing the status palette is for.
    /// </summary>
    private static void Recolour(TextBlock target, string brushKey)
    {
        if (ThemeBrush(target, brushKey) is Brush brush)
        {
            target.Foreground = brush;
        }
    }

    private void DrawSparkline()
    {
        CanvasSparkline.Children.Clear();
        if (_sparklineBuffer.Count < 2)
        {
            // An empty plot area otherwise reads as a flat line at zero.
            TxtSparklineEmpty.Visibility = Visibility.Visible;
            return;
        }

        TxtSparklineEmpty.Visibility = Visibility.Collapsed;

        double width = CanvasSparkline.ActualWidth > 0 ? CanvasSparkline.ActualWidth : 350;
        double height = CanvasSparkline.ActualHeight > 0 ? CanvasSparkline.ActualHeight : 45;

        double min = _sparklineBuffer.Min();
        double max = _sparklineBuffer.Max();
        if (min == max) { min -= 1.0; max += 1.0; }

        double stepX = width / (_sparklineBuffer.Count - 1);
        Polyline polyline = new Polyline { StrokeThickness = 1.5 };
        if (ThemeBrush(this, "Series1Brush") is Brush stroke)
        {
            polyline.Stroke = stroke;
        }

        for (int i = 0; i < _sparklineBuffer.Count; i++)
        {
            double x = i * stepX;
            double y = height - ((_sparklineBuffer[i] - min) / (max - min)) * (height - 8) - 4;
            polyline.Points.Add(new Point(x, y));
        }

        CanvasSparkline.Children.Add(polyline);
    }

    /// <summary>Switches one of the profile's nodes, addressing it by the id the profile gave it.</summary>
    private void SendNodePowerCommand(NodePowerToggle node, bool on) =>
        SendCommand($"NODE_POWER {node.Id} {(on ? "ON" : "OFF")}");

    private void BtnZeroCalibration_Click(object sender, RoutedEventArgs e)
    {
        LogMessage("CALIB", "Triggered zero offset calibration across all nodes.");
        SendCommand("ZERO_CALIBRATION_ALL");
    }

    private void BtnBurstMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton btn)
        {
            bool state = btn.IsChecked == true;
            // The caption states the rate now in force rather than the one the button would set,
            // so it agrees with the command that was actually sent below.
            btn.Content = state ? "Burst: 1000 Hz" : "Burst: 1 Hz";
            SendCommand($"BURST_MODE {(state ? "1000HZ" : "1HZ")}");
        }
    }

    private void QuickCmd_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string cmd)
        {
            SendCommand(cmd);
        }
    }

    private void BtnSendCustomCmd_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TxtCustomCmd.Text))
        {
            SendCommand(TxtCustomCmd.Text.Trim());
            TxtCustomCmd.Clear();
        }
    }

    private void TxtCustomCmd_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(TxtCustomCmd.Text))
        {
            SendCommand(TxtCustomCmd.Text.Trim());
            TxtCustomCmd.Clear();
        }
    }

    private void SendCommand(string cmd)
    {
        LogMessage("TX", $"Sending command: {cmd}");

        // The footer used to read "System Status: Ready" forever, because nothing ever wrote to it.
        // It now reports the one thing this panel actually knows about the system.
        TxtSystemStatus.Text = $"Last command sent {DateTime.Now:HH:mm:ss} — {cmd}";
        OnCommandSent?.Invoke(cmd);
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        _eventLogEntries.Clear();
    }

    /// <summary>Keeps the event log's auto-scroll inside the event log.</summary>
    /// <remarks>
    /// <see cref="ListBox.ScrollIntoView"/> works by asking the newest row to bring itself into
    /// view, and that request keeps bubbling after the list has scrolled. Every ScrollViewer above
    /// it then answers the same request by scrolling the log into view too — so at the telemetry
    /// rate the panel crept downward on its own and settled part-way through its own content, with
    /// the live readings and the node controls above the fold. The list has already done what was
    /// asked by the time the event reaches here, so nothing above it needs to act on it.
    /// </remarks>
    private void DgEventLog_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
    }
}
