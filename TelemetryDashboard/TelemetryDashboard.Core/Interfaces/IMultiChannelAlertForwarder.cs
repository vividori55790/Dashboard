using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Analytics;

namespace TelemetryDashboard.Core.Interfaces;

/// <summary>
/// Interface for Multi-Channel Alert Forwarder.
/// Dispatches telemetry anomaly notifications across Slack (Block Kit), Discord (Embeds),
/// Telegram Bot API, and generic HTTP Webhooks with embedded waveform snapshots and throttling.
/// </summary>
public interface IMultiChannelAlertForwarder
{
    AlertChannelConfig Config { get; set; }

    Task<bool> DispatchAlertAsync(AnomalyResult anomaly, string aiDiagnosisText = "", IEnumerable<double>? recentSamples = null, CancellationToken cancellationToken = default);

    string FormatSlackPayload(AnomalyResult anomaly, string aiDiagnosis, string sparkline);
    string FormatDiscordPayload(AnomalyResult anomaly, string aiDiagnosis, string sparkline);
    string FormatTelegramPayload(AnomalyResult anomaly, string aiDiagnosis);
    string FormatGenericWebhookPayload(AnomalyResult anomaly, string aiDiagnosis, string sparkline, string svgWaveform, WaveformStats stats);

    bool ShouldThrottle(string channelName, double zScore, DateTime timestamp);
    void ResetThrottling();
}
