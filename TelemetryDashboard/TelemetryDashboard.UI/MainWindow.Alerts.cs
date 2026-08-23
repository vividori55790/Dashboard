using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TelemetryDashboard.UI.ViewModels;

namespace TelemetryDashboard.UI;

/// <summary>
/// Making an alarm something the operator notices.
/// </summary>
/// <remarks>
/// What an alarm did before was add a line to the event log, which is a scrolling panel beside the
/// chart the operator is actually watching — and on the hardware path it did not even do that. The
/// analytics engine scored every reading and its verdict was read only by the simulated path.
/// <para>
/// <see cref="AlertUXService"/> shipped complete for this: a spoken queue with a ceiling so a
/// cascade cannot recite history over a live incident, speech text sanitised of the XML characters
/// a synthesiser would swallow the rest of the sentence on, and a toast that still fires when
/// speech is unavailable so alerting degrades rather than fails. Nothing constructed it, and the
/// System.Speech package it was written against was referenced by the project and used by no file.
/// </para>
/// </remarks>
public partial class MainWindow
{
    private readonly AlertUXService _alerts = new();
    private DispatcherTimer? _alertDismissTimer;

    /// <summary>Synthesiser, or null when this machine has none.</summary>
    /// <remarks>
    /// Held as object so this file does not force the assembly to load on a machine without it.
    /// </remarks>
    private System.Speech.Synthesis.SpeechSynthesizer? _voice;

    /// <summary>
    /// Connects the alarm source to the alarm surfaces, and finds out whether speech is possible.
    /// </summary>
    private void SetupAlerts()
    {
        ControlPanel.AlertRaised += OnAlertRaised;

        try
        {
            var synthesiser = new System.Speech.Synthesis.SpeechSynthesizer();

            // A synthesiser with no installed voice constructs and then says nothing, which is the
            // failure this service's DisableSapiTts exists for: better a station that knows it is
            // silent than one that believes it announced something.
            if (synthesiser.GetInstalledVoices().Count == 0)
            {
                synthesiser.Dispose();
                DisableSpeech("no speech voice is installed");
                return;
            }

            _voice = synthesiser;
            ControlPanel.LogMessage("SYSTEM",
                $"Spoken alerts available ({synthesiser.GetInstalledVoices().Count} voice(s)).");
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or InvalidOperationException
                                      or System.IO.FileNotFoundException or TypeInitializationException)
        {
            DisableSpeech(ex.GetType().Name);
        }
    }

    private void DisableSpeech(string reason)
    {
        _alerts.DisableSapiTts();
        ControlPanel.LogMessage("SYSTEM",
            $"Spoken alerts off ({reason}). Alarms still raise the banner.");
    }

    /// <summary>Shows the banner, and speaks when the alert is critical and speech is available.</summary>
    private void OnAlertRaised(string message, bool isCritical)
    {
        if (!_alerts.TriggerAlert(message, isCritical)) return;

        ShowAlertBanner(_alerts.LastToastMessage, _alerts.LastAlertWasCritical);
        SpeakPending();
    }

    /// <summary>
    /// Drains what the service queued, one utterance at a time.
    /// </summary>
    /// <remarks>
    /// The service holds the queue and the ceiling; this only reads it. Speaking asynchronously
    /// matters more than it looks: the synthesiser blocks its caller, and this runs on the
    /// dispatcher, so a synchronous call would freeze the window for the length of the sentence —
    /// during the incident the sentence is about.
    /// </remarks>
    private void SpeakPending()
    {
        if (_voice is null || _alerts.PendingVoiceCount == 0) return;

        try
        {
            _voice.SpeakAsyncCancelAll();
            _voice.SpeakAsync(_alerts.TakeNextVoiceAlert() ?? string.Empty);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The audio device went away mid-run. Stop trying rather than throwing once per alarm.
            _voice.Dispose();
            _voice = null;
            DisableSpeech(ex.GetType().Name);
        }
    }

    /// <summary>
    /// Raises the banner, and decides whether it goes away on its own.
    /// </summary>
    /// <remarks>
    /// A critical alert stays until it is dismissed. An alarm that disappears while the operator is
    /// looking at the plant is an alarm they can be shown and still never see, and acknowledging is
    /// the point of raising it.
    /// </remarks>
    private void ShowAlertBanner(string message, bool isCritical)
    {
        AlertBannerText.Text = message;
        AlertBanner.SetResourceReference(BackgroundProperty,
            isCritical ? "DangerSubtleBrush" : "WarningSubtleBrush");
        AlertBanner.SetResourceReference(BorderBrushProperty,
            isCritical ? "DangerBrush" : "WarningBrush");
        AlertBannerIcon.SetResourceReference(ForegroundProperty,
            isCritical ? "DangerBrush" : "WarningBrush");
        AlertBanner.Visibility = Visibility.Visible;

        _alertDismissTimer?.Stop();
        _alertDismissTimer = null;

        if (isCritical) return;

        _alertDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _alertDismissTimer.Tick += (_, _) => DismissAlertBanner();
        _alertDismissTimer.Start();
    }

    private void DismissAlertBanner()
    {
        _alertDismissTimer?.Stop();
        _alertDismissTimer = null;
        AlertBanner.Visibility = Visibility.Collapsed;
    }

    private void BtnDismissAlert_Click(object sender, RoutedEventArgs e) => DismissAlertBanner();
}
