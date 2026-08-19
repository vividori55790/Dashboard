using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;

namespace TelemetryDashboard.UI.Dialogs;

public class SamplingChannelRow
{
    public string ChannelName { get; set; } = string.Empty;
    public string CurrentRate { get; set; } = "1 Hz";
    public string Mode { get; set; } = "🟢 NOMINAL";
    public string LastZScore { get; set; } = "-";
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Adaptive sampling configuration and live state.
/// </summary>
/// <remarks>
/// Every row is read back from <see cref="AdaptiveSamplingController"/> after it evaluates the
/// injected score. The dialog previously displayed a fixed table of literals, so the grid showed
/// "1,000 Hz BURST" whether or not the state machine had actually switched — the one thing this
/// screen exists to confirm.
/// </remarks>
public partial class AdaptiveSamplingDialog : Window
{
    private static readonly string[] Channels =
    {
        "COM3.Temperature", "COM3.Vibration", "COM4.Voltage", "COM4.Current"
    };

    private readonly AdaptiveSamplingController _controller;
    private readonly Dictionary<string, double> _lastScores = new(StringComparer.OrdinalIgnoreCase);

    public AdaptiveSamplingDialog() : this(new AdaptiveSamplingController())
    {
    }

    /// <summary>Binds the dialog to the controller the application is actually running.</summary>
    public AdaptiveSamplingDialog(AdaptiveSamplingController controller)
    {
        InitializeComponent();
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));

        TxtBaseRate.Text = _controller.BaseRateHz.ToString(CultureInfo.InvariantCulture);
        TxtBurstRate.Text = _controller.BurstRateHz.ToString(CultureInfo.InvariantCulture);
        TxtThreshold.Text = _controller.AnomalyThresholdSigma.ToString(CultureInfo.InvariantCulture);
        TxtCooldown.Text = _controller.CooldownDurationSec.ToString(CultureInfo.InvariantCulture);

        RefreshView();
    }

    /// <summary>Renders one row per channel straight from controller state.</summary>
    private void RefreshView()
    {
        var rows = new List<SamplingChannelRow>(Channels.Length);

        foreach (string channel in Channels)
        {
            SamplingMode mode = _controller.GetSamplingMode(channel);
            int rate = _controller.GetSamplingRate(channel);
            bool hasScore = _lastScores.TryGetValue(channel, out double score);

            rows.Add(new SamplingChannelRow
            {
                ChannelName = channel,
                CurrentRate = $"{rate:N0} Hz",
                Mode = mode switch
                {
                    SamplingMode.Burst => "⚡ BURST",
                    SamplingMode.Cooldown => "🕒 COOLDOWN",
                    _ => "🟢 NOMINAL"
                },
                LastZScore = hasScore ? $"{score:F1}σ" : "-",
                Status = mode switch
                {
                    SamplingMode.Burst => "🚨 이상 파형 고속 캡처 중",
                    SamplingMode.Cooldown => "쿨다운 유지 중",
                    _ => "정상 저전력 로깅"
                }
            });
        }

        DgChannelSampling.ItemsSource = rows;

        bool anyBurst = rows.Any(r => r.Mode.Contains("BURST") || r.Mode.Contains("COOLDOWN"));
        TxtCurrentMode.Text = anyBurst
            ? $"모드: ⚡ BURST MODE ({_controller.BurstRateHz:N0} Hz 활성)"
            : $"모드: 🟢 NOMINAL ({_controller.BaseRateHz:N0} Hz)";
        TxtCurrentMode.Foreground = new SolidColorBrush(anyBurst
            ? Color.FromRgb(0xFF, 0x2E, 0x63)
            : Color.FromRgb(0x00, 0xFF, 0x9D));
    }

    private void BtnTriggerSurge_Click(object sender, RoutedEventArgs e)
    {
        ApplySettings();

        // Inject a score past the threshold on the temperature and vibration channels only,
        // so the grid demonstrates the controller discriminating between channels.
        double surge = _controller.AnomalyThresholdSigma + 1.3;
        Evaluate("COM3.Temperature", surge);
        Evaluate("COM3.Vibration", surge - 0.6);
        Evaluate("COM4.Voltage", 0.2);
        Evaluate("COM4.Current", 0.3);

        RefreshView();

        int burstRate = _controller.GetSamplingRate("COM3.Temperature");
        TxtTestStatus.Text =
            $"[{DateTime.Now:HH:mm:ss}] {surge:F1}σ 주입 ➔ COM3.Temperature 실제 전환 결과: {burstRate:N0} Hz";
    }

    private void BtnResetNormal_Click(object sender, RoutedEventArgs e)
    {
        // Clearing state is what actually returns the controller to nominal; a cooldown window
        // would otherwise legitimately hold the burst rate.
        _controller.ResetAll();
        _lastScores.Clear();
        RefreshView();

        TxtTestStatus.Text = $"[{DateTime.Now:HH:mm:ss}] 모든 채널 {_controller.BaseRateHz:N0} Hz 평시 샘플링으로 복귀했습니다.";
    }

    private void Evaluate(string channel, double zScore)
    {
        _lastScores[channel] = zScore;
        _controller.EvaluateSamplingRate(channel, zScore);
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        ApplySettings();
        RefreshView();

        MessageBox.Show(this,
            $"가변 샘플링 파라미터를 적용했습니다.\n\n" +
            $"• 평시: {_controller.BaseRateHz:N0} Hz\n" +
            $"• 버스트: {_controller.BurstRateHz:N0} Hz\n" +
            $"• 임계치: {_controller.AnomalyThresholdSigma:F2}σ\n" +
            $"• 쿨다운: {_controller.CooldownDurationSec:F1}s",
            "적용 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private void ApplySettings()
    {
        if (int.TryParse(TxtBaseRate.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int baseRate) && baseRate > 0)
        {
            _controller.BaseRateHz = baseRate;
        }
        if (int.TryParse(TxtBurstRate.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int burst) && burst > 0)
        {
            _controller.BurstRateHz = burst;
        }
        if (double.TryParse(TxtThreshold.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double threshold) && threshold > 0)
        {
            _controller.AnomalyThresholdSigma = threshold;
        }
        if (double.TryParse(TxtCooldown.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double cooldown) && cooldown >= 0)
        {
            _controller.CooldownDurationSec = cooldown;
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
