using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Analytics;

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
}

public partial class ControlPanelControl : UserControl
{
    public event Action<string>? OnCommandSent;

    private readonly ObservableCollection<EventLogEntry> _eventLogEntries = new();
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
        LogMessage("SYSTEM", "Control panel initialized with structured DataGrid & Z-Score breakdown. Ready.");
    }

    public void LogMessage(string tag, string message)
    {
        Dispatcher.Invoke(() =>
        {
            string timeStr = DateTime.Now.ToString("HH:mm:ss.fff");
            string level = "INFO";
            if (tag.Contains("ERR") || tag.Contains("CRIT") || message.Contains("CRITICAL")) level = "CRIT";
            else if (tag.Contains("WARN") || tag.Contains("SIM")) level = "WARN";

            string node = "COM3";
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

            if (temperature.HasVerdict && temperature.ZScore >= 3.5)
            {
                LogTelemetryEvent("COM3", "Temp", temp, temperature.ZScore,
                    $"CRITICAL thermal anomaly (Z={temperature.ZScore:F1}σ)");
            }

            _sparklineBuffer.Add(temp);
            if (_sparklineBuffer.Count > 50) _sparklineBuffer.RemoveAt(0);
            DrawSparkline();
        });
    }

    /// <summary>Updates the panel for a single channel arriving from real hardware.</summary>
    public void UpdateChannelStats(string node, string variable, double value, AnomalyResult analysis)
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
        });
    }

    /// <summary>Counts a packet and refreshes the throughput readouts.</summary>
    private void RegisterPacket()
    {
        _totalPackets++;
        _bytesSinceRateReset += EstimatedPacketBytes;

        TxtPacketCount.Text = $"{_totalPackets:N0} pkts";

        // Throughput over the elapsed interval. The old readout divided the cumulative packet
        // count by 1024 and labelled it KB/s, so the "rate" only ever climbed.
        double elapsed = (DateTime.Now - _rateWindowStart).TotalSeconds;
        if (elapsed >= 1.0)
        {
            TxtDataRate.Text = $"{_bytesSinceRateReset / 1024.0 / elapsed:F1} KB/s";
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
            target.Text = $"— [{analysis.SampleCount} samples, no baseline yet]";
            target.Foreground = new SolidColorBrush(Color.FromRgb(136, 146, 176));
            return;
        }

        ApplyZScore(target, analysis.ZScore);
    }

    /// <summary>Renders a z-score with the severity banding the alert pipeline uses.</summary>
    private static void ApplyZScore(TextBlock target, double zScore)
    {
        if (zScore >= 3.5)
        {
            target.Text = $"{zScore:F1}σ [CRITICAL]";
            target.Foreground = new SolidColorBrush(Color.FromRgb(255, 46, 99));
        }
        else if (zScore >= 2.0)
        {
            target.Text = $"{zScore:F1}σ [WARNING]";
            target.Foreground = new SolidColorBrush(Color.FromRgb(255, 234, 0));
        }
        else
        {
            target.Text = $"{zScore:F1}σ [NORMAL]";
            target.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
        }
    }

    private void DrawSparkline()
    {
        CanvasSparkline.Children.Clear();
        if (_sparklineBuffer.Count < 2) return;

        double width = CanvasSparkline.ActualWidth > 0 ? CanvasSparkline.ActualWidth : 350;
        double height = CanvasSparkline.ActualHeight > 0 ? CanvasSparkline.ActualHeight : 45;

        double min = _sparklineBuffer.Min();
        double max = _sparklineBuffer.Max();
        if (min == max) { min -= 1.0; max += 1.0; }

        double stepX = width / (_sparklineBuffer.Count - 1);
        Polyline polyline = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(102, 252, 241)),
            StrokeThickness = 1.5
        };

        for (int i = 0; i < _sparklineBuffer.Count; i++)
        {
            double x = i * stepX;
            double y = height - ((_sparklineBuffer[i] - min) / (max - min)) * (height - 8) - 4;
            polyline.Points.Add(new Point(x, y));
        }

        CanvasSparkline.Children.Add(polyline);
    }

    private void BtnNodeCom3Power_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton btn)
        {
            bool state = btn.IsChecked == true;
            btn.Content = $"Node COM3 (DAB): {(state ? "ON" : "OFF")}";
            SendCommand($"NODE_POWER COM3 {(state ? "ON" : "OFF")}");
        }
    }

    private void BtnNodeCom4Power_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton btn)
        {
            bool state = btn.IsChecked == true;
            btn.Content = $"Node COM4 (PSFB): {(state ? "ON" : "OFF")}";
            SendCommand($"NODE_POWER COM4 {(state ? "ON" : "OFF")}");
        }
    }

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
            btn.Content = state ? "⚡ 1000Hz BURST ACTIVE" : "⚡ 1000Hz Burst";
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
        OnCommandSent?.Invoke(cmd);
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        _eventLogEntries.Clear();
    }
}
