using System;
using System.Windows;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Analytics;

namespace TelemetryDashboard.UI.Dialogs;

public partial class MlAnalyticsDialog : Window
{
    private readonly TelemetryMlAnalyticsEngine _mlEngine;

    public MlAnalyticsDialog(TelemetryMlAnalyticsEngine mlEngine)
    {
        InitializeComponent();
        _mlEngine = mlEngine;
        SldThreshold.Value = _mlEngine.ZScoreThreshold;

        PopulateSampleAnalytics();
    }

    private void PopulateSampleAnalytics()
    {
        LstAnomalyScores.Items.Clear();
        LstForecasts.Items.Clear();

        string[] channels = new[] { "COM3.Temperature", "COM3.Humidity", "COM3.Vibration", "COM4.RPM" };
        Random rng = new();

        foreach (var ch in channels)
        {
            double val = ch.Contains("Temp") ? 26.5 : (ch.Contains("Hum") ? 52.0 : (ch.Contains("Vib") ? 0.12 : 1250));
            var res = _mlEngine.AnalyzeChannel(ch, val + (rng.NextDouble() - 0.5) * 2);

            string statusStr = res.IsAnomaly ? "🚨 CRITICAL ANOMALY" : "✅ NORMAL";
            LstAnomalyScores.Items.Add($"[{res.ChannelName}] Val: {res.CurrentValue:F2} | Mean: {res.Mean:F2} | Z-Score: {res.ZScore:F2} -> {statusStr}");

            string breachInfo = res.EstimatedTimeToBreachSec > 0 ? $"⚠️ Warning in {res.EstimatedTimeToBreachSec:F0}s" : "Stable";
            LstForecasts.Items.Add($"[{res.ChannelName}] Predicted in 60s: {res.PredictedValueIn60s:F2} ({breachInfo})");
        }
    }

    private void SldThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_mlEngine != null && TxtThresholdVal != null)
        {
            _mlEngine.ZScoreThreshold = e.NewValue;
            TxtThresholdVal.Text = $"{e.NewValue:F1} σ (Standard Deviations)";
            PopulateSampleAnalytics();
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
