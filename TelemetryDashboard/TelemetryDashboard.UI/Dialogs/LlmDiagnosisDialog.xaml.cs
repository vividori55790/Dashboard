using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TelemetryDashboard.Core.Services;

using TelemetryDashboard.UI.Diagnostics;
using TelemetryDashboard.Core.Analytics;

namespace TelemetryDashboard.UI.Dialogs;

public partial class LlmDiagnosisDialog : Window
{
    private readonly LlmDiagnosisAgent _agent = new();
    private readonly Action<string>? _onCommandSend;
    private readonly LlmApiConfig _config = new();

    /// <summary>Supplies the anomalies the running application has actually recorded.</summary>
    private readonly Func<IReadOnlyList<AnomalyResult>>? _recentAnomalies;

    public LlmDiagnosisDialog(
        Action<string>? onCommandSend = null,
        Func<IReadOnlyList<AnomalyResult>>? recentAnomalies = null)
    {
        InitializeComponent();
        _onCommandSend = onCommandSend;
        _recentAnomalies = recentAnomalies;
        _ = RunDiagnosisAsync("최근 온도 스파이크와 전압 강하 원인을 분석하고 60초 후 예측치를 알려줘");
    }

    private void BtnToggleConfig_Click(object sender, RoutedEventArgs e)
    {
        BorderConfig.Visibility = BorderConfig.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CboProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TxtEndpointUrl == null || TxtModelName == null) return;

        string selected = (CboProvider.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Offline";
        if (selected.Contains("Ollama"))
        {
            _config.Provider = "Ollama";
            TxtEndpointUrl.Text = "http://localhost:11434/v1/chat/completions";
            TxtModelName.Text = "llama3:latest";
            _config.ApiKey = "";
        }
        else if (selected.Contains("OpenAI"))
        {
            _config.Provider = "OpenAI";
            TxtEndpointUrl.Text = "https://api.openai.com/v1/chat/completions";
            TxtModelName.Text = "gpt-4o-mini";
        }
        else if (selected.Contains("Custom"))
        {
            _config.Provider = "Custom";
        }
        else
        {
            _config.Provider = "Offline";
        }
    }

    private async void BtnRunQuery_Click(object sender, RoutedEventArgs e)
    {
        await RunDiagnosisAsync(TxtQuery.Text);
    }

    private async void TxtQuery_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await RunDiagnosisAsync(TxtQuery.Text);
        }
    }

    private async void QuickQuery_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string query)
        {
            TxtQuery.Text = query;
            await RunDiagnosisAsync(query);
        }
    }

    private async Task RunDiagnosisAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;

        BtnRunQuery.IsEnabled = false;
        TxtInferenceEngine.Text = "엔진 — 분석 중";

        // Collect config values
        _config.ApiKey = TxtApiKey.Text.Trim();
        _config.EndpointUrl = TxtEndpointUrl.Text.Trim();
        _config.ModelName = TxtModelName.Text.Trim();

        // Diagnose what the engine actually recorded. Sending a fixed anomaly set produced a
        // report about telemetry this system had never seen, while presenting it as live analysis.
        IReadOnlyList<AnomalyResult> anomalies = _recentAnomalies?.Invoke() ?? Array.Empty<AnomalyResult>();
        bool usingLiveData = anomalies.Count > 0;

        if (!usingLiveData)
        {
            anomalies = DemonstrationPayloads.DiagnosisFallback();
        }

        var report = await _agent.ProcessQueryWithLlmApiAsync(query, anomalies.ToList(), _config);

        TxtReportTimestamp.Text = usingLiveData
            ? $"{DateTime.Now:HH:mm:ss} 생성 · 실측 텔레메트리, 이상 징후 {anomalies.Count}건"
            : $"{DateTime.Now:HH:mm:ss} 생성 · 기록된 이상 징후 없음, 예시 데이터로 작성";
        TxtReportMarkdown.Text = report.MarkdownReport;
        TxtInferenceEngine.Text = $"엔진 — {_config.Provider} ({_config.ModelName})";

        // Status colours come from the palette and are spent only on a verdict the engine returned.
        // They used to be four literal RGB values chosen here, in a window whose every other colour
        // was defined somewhere else.
        bool critical = report.SeverityLevel == "CRITICAL" || anomalies.Any(a => a.ZScore >= 3.5);
        TxtSeverity.Text = critical ? "심각도 — 위험, 조치 필요" : "심각도 — 정상";
        BadgeSeverity.SetResourceReference(BackgroundProperty,
            critical ? "DangerSubtleBrush" : "SuccessSubtleBrush");
        BadgeSeverity.SetResourceReference(BorderBrushProperty,
            critical ? "DangerBrush" : "SuccessBrush");
        TxtSeverity.SetResourceReference(ForegroundProperty, critical ? "DangerBrush" : "SuccessBrush");

        BtnRunQuery.IsEnabled = true;
    }

    private void BtnTriggerEmergencyAction_Click(object sender, RoutedEventArgs e)
    {
        string cmd = "$CMD,SAFE_MODE,NODE_1,THROTTLE_50";
        _onCommandSend?.Invoke(cmd);
        TxtEmergencyStatus.Text = $"{DateTime.Now:HH:mm:ss} 전송: {cmd}";
        TxtEmergencyStatus.SetResourceReference(ForegroundProperty, "WarningBrush");
        MessageBox.Show(this,
            $"긴급 보호 명령을 전송했습니다.\n\n{cmd}\n\n장비가 명령을 받아들였는지는 이벤트 로그에서 확인하십시오.",
            "긴급 보호 명령", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void BtnCopyReport_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(TxtReportMarkdown.Text);
        MessageBox.Show(this, "AI Diagnostic Report copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnSaveReport_Click(object sender, RoutedEventArgs e)
    {
        var sfd = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Markdown Report (*.md)|*.md|All Files (*.*)|*.*",
            FileName = $"AI_Incident_Diagnosis_{DateTime.Now:yyyyMMdd_HHmmss}.md"
        };
        if (sfd.ShowDialog() == true)
        {
            File.WriteAllText(sfd.FileName, TxtReportMarkdown.Text, Encoding.UTF8);
            MessageBox.Show(this, $"Report saved successfully to:\n{sfd.FileName}", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
