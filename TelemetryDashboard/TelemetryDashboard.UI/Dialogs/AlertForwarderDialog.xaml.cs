using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Infrastructure.Integrations;

using TelemetryDashboard.UI.Diagnostics;

namespace TelemetryDashboard.UI.Dialogs;

public partial class AlertForwarderDialog : Window
{
    private readonly MultiChannelAlertForwarder _forwarder = new();

    public AlertForwarderDialog()
    {
        InitializeComponent();
    }

    private async void BtnTestSlack_Click(object sender, RoutedEventArgs e)
    {
        Log("Sending test payload to Slack Block Kit endpoint...");
        _forwarder.Config.SlackWebhookUrl = TxtSlackWebhook.Text;
        _forwarder.Config.EnableSlack = true;
        await DispatchTest("Slack");
    }

    private async void BtnTestDiscord_Click(object sender, RoutedEventArgs e)
    {
        Log("Sending test payload to Discord Embeds endpoint...");
        _forwarder.Config.DiscordWebhookUrl = TxtDiscordWebhook.Text;
        _forwarder.Config.EnableDiscord = true;
        await DispatchTest("Discord");
    }

    private async void BtnTestTelegram_Click(object sender, RoutedEventArgs e)
    {
        Log("Sending test payload to Telegram Bot endpoint...");
        _forwarder.Config.TelegramBotToken = TxtTelegramToken.Text;
        _forwarder.Config.TelegramChatId = TxtTelegramChatId.Text;
        _forwarder.Config.EnableTelegram = true;
        await DispatchTest("Telegram");
    }

    private async void BtnTestWebhook_Click(object sender, RoutedEventArgs e)
    {
        Log("Sending test payload to Generic Webhook endpoint...");
        _forwarder.Config.GenericWebhookUrl = TxtGenericWebhook.Text;
        _forwarder.Config.EnableGenericWebhook = true;
        await DispatchTest("Generic Webhook");
    }

    private async Task DispatchTest(string channelName)
    {
        // Synthetic payload, clearly marked so a recipient cannot mistake it for a live incident.
        var sampleWaveform = DemonstrationPayloads.AlertDeliveryWaveform();
        var anomaly = DemonstrationPayloads.AlertDeliveryProbe(channelName);

        bool success = await _forwarder.DispatchAlertAsync(
            anomaly: anomaly,
            aiDiagnosisText: DemonstrationPayloads.AlertDeliveryDiagnosis,
            recentSamples: sampleWaveform
        );

        Log($"[{DateTime.Now:HH:mm:ss}] {channelName} Test Dispatched -> Result: {(success ? "SUCCESS" : "THROTTLED / FAILED")}");
    }

    private void Log(string message)
    {
        TxtDispatchLog.Text += $"\n{message}";
        TxtDispatchLog.ScrollToEnd();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        _forwarder.Config.SlackWebhookUrl = TxtSlackWebhook.Text;
        _forwarder.Config.DiscordWebhookUrl = TxtDiscordWebhook.Text;
        _forwarder.Config.TelegramBotToken = TxtTelegramToken.Text;
        _forwarder.Config.TelegramChatId = TxtTelegramChatId.Text;
        _forwarder.Config.GenericWebhookUrl = TxtGenericWebhook.Text;
        _forwarder.Config.EnableSlack = ChkSlack.IsChecked == true;
        _forwarder.Config.EnableDiscord = ChkDiscord.IsChecked == true;
        _forwarder.Config.EnableTelegram = ChkTelegram.IsChecked == true;
        _forwarder.Config.EnableGenericWebhook = ChkWebhook.IsChecked == true;

        MessageBox.Show(this, "Alert Forwarder settings saved successfully!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
