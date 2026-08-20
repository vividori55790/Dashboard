using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Integrations;

using TelemetryDashboard.UI.Diagnostics;

namespace TelemetryDashboard.UI.Dialogs;

/// <summary>
/// Destination settings for anomaly alerts, with a per-destination delivery test.
/// </summary>
/// <remarks>
/// The fields open from the forwarder's own config. They used to open pre-filled with real-looking
/// literals — a Slack webhook path, a bot token, an internal ERP URL — so the dialog claimed four
/// configured destinations on a system that had none.
/// </remarks>
public partial class AlertForwarderDialog : Window
{
    private readonly MultiChannelAlertForwarder _forwarder;
    private readonly bool _boundToRunningForwarder;

    /// <param name="forwarder">
    /// The forwarder the application is running. When omitted the dialog works against its own
    /// instance and says so on save, rather than reporting a change nothing received.
    /// </param>
    public AlertForwarderDialog(MultiChannelAlertForwarder? forwarder = null)
    {
        InitializeComponent();

        _boundToRunningForwarder = forwarder is not null;
        _forwarder = forwarder ?? new MultiChannelAlertForwarder();

        AlertChannelConfig config = _forwarder.Config;
        TxtSlackWebhook.Text = config.SlackWebhookUrl;
        TxtDiscordWebhook.Text = config.DiscordWebhookUrl;
        TxtTelegramToken.Text = config.TelegramBotToken;
        TxtTelegramChatId.Text = config.TelegramChatId;
        TxtGenericWebhook.Text = config.GenericWebhookUrl;
        ChkSlack.IsChecked = config.EnableSlack;
        ChkDiscord.IsChecked = config.EnableDiscord;
        ChkTelegram.IsChecked = config.EnableTelegram;
        ChkWebhook.IsChecked = config.EnableGenericWebhook;

        // The footer used to announce "60s throttling active" while the forwarder's cooldown was
        // whatever the config said. Read both numbers from the object that enforces them.
        TxtThrottleSummary.Text =
            $"같은 채널 재알림 간격 {config.ThrottleCooldownSec:F0}초, " +
            $"Z-Score가 {config.MinZScoreJumpForBypass:F1} 이상 뛰면 간격을 무시하고 전송합니다.";

        TxtDispatchLog.Text = "전송 기록이 여기에 표시됩니다.";
    }

    private async void BtnTestSlack_Click(object sender, RoutedEventArgs e)
    {
        _forwarder.Config.SlackWebhookUrl = TxtSlackWebhook.Text.Trim();
        _forwarder.Config.EnableSlack = true;
        await DispatchTest("Slack", _forwarder.Config.SlackWebhookUrl);
    }

    private async void BtnTestDiscord_Click(object sender, RoutedEventArgs e)
    {
        _forwarder.Config.DiscordWebhookUrl = TxtDiscordWebhook.Text.Trim();
        _forwarder.Config.EnableDiscord = true;
        await DispatchTest("Discord", _forwarder.Config.DiscordWebhookUrl);
    }

    private async void BtnTestTelegram_Click(object sender, RoutedEventArgs e)
    {
        _forwarder.Config.TelegramBotToken = TxtTelegramToken.Text.Trim();
        _forwarder.Config.TelegramChatId = TxtTelegramChatId.Text.Trim();
        _forwarder.Config.EnableTelegram = true;

        string target = string.IsNullOrWhiteSpace(_forwarder.Config.TelegramChatId)
            ? string.Empty
            : _forwarder.Config.TelegramBotToken;
        await DispatchTest("Telegram", target);
    }

    private async void BtnTestWebhook_Click(object sender, RoutedEventArgs e)
    {
        _forwarder.Config.GenericWebhookUrl = TxtGenericWebhook.Text.Trim();
        _forwarder.Config.EnableGenericWebhook = true;
        await DispatchTest("HTTP 웹훅", _forwarder.Config.GenericWebhookUrl);
    }

    /// <summary>Sends one clearly-marked synthetic alert and reports only what can be observed.</summary>
    private async Task DispatchTest(string channelName, string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            Log($"{channelName}: 주소가 비어 있어 전송하지 않았습니다.");
            return;
        }

        Log($"{channelName}: 테스트 알림 전송 중...");

        // Synthetic payload, clearly marked so a recipient cannot mistake it for a live incident.
        var sampleWaveform = DemonstrationPayloads.AlertDeliveryWaveform();
        var anomaly = DemonstrationPayloads.AlertDeliveryProbe(channelName);

        bool dispatched = await _forwarder.DispatchAlertAsync(
            anomaly, DemonstrationPayloads.AlertDeliveryDiagnosis, sampleWaveform);

        // DispatchAlertAsync reports whether the send was attempted, not whether it arrived: the
        // HTTP post swallows transport errors. "SUCCESS" claimed a delivery nobody confirmed.
        Log(dispatched
            ? $"{channelName}: 요청을 보냈습니다. 실제 도착 여부는 수신 채널에서 확인하세요."
            : $"{channelName}: 재알림 간격에 걸려 보내지 않았습니다.");
    }

    private void Log(string message)
    {
        TxtDispatchLog.Text += $"\n[{DateTime.Now:HH:mm:ss}] {message}";
        TxtDispatchLog.ScrollToEnd();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        _forwarder.Config.SlackWebhookUrl = TxtSlackWebhook.Text.Trim();
        _forwarder.Config.DiscordWebhookUrl = TxtDiscordWebhook.Text.Trim();
        _forwarder.Config.TelegramBotToken = TxtTelegramToken.Text.Trim();
        _forwarder.Config.TelegramChatId = TxtTelegramChatId.Text.Trim();
        _forwarder.Config.GenericWebhookUrl = TxtGenericWebhook.Text.Trim();
        _forwarder.Config.EnableSlack = ChkSlack.IsChecked == true;
        _forwarder.Config.EnableDiscord = ChkDiscord.IsChecked == true;
        _forwarder.Config.EnableTelegram = ChkTelegram.IsChecked == true;
        _forwarder.Config.EnableGenericWebhook = ChkWebhook.IsChecked == true;

        MessageBox.Show(this,
            _boundToRunningForwarder
                ? "실행 중인 알림 전달기에 설정을 적용했습니다."
                : "이 창의 테스트 전송에만 적용했습니다. 애플리케이션에는 저장되지 않습니다.",
            "설정 적용", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Escape closes the dialog, as in every other dialog here.</summary>
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }
}
