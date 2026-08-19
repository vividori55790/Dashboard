using System;

namespace TelemetryDashboard.Core.Models;

/// <summary>
/// Summary statistics for waveform snapshots.
/// </summary>
public class WaveformStats
{
    public double Min { get; set; }
    public double Max { get; set; }
    public double Mean { get; set; }
    public double PeakToPeak { get; set; }
    public double StdDev { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Configuration for Multi-Channel Alert Forwarder endpoints and throttling.
/// </summary>
public class AlertChannelConfig
{
    public string SlackWebhookUrl { get; set; } = string.Empty;
    public string DiscordWebhookUrl { get; set; } = string.Empty;
    public string TelegramBotToken { get; set; } = string.Empty;
    public string TelegramChatId { get; set; } = string.Empty;
    public string GenericWebhookUrl { get; set; } = string.Empty;

    public bool EnableSlack { get; set; } = true;
    public bool EnableDiscord { get; set; } = true;
    public bool EnableTelegram { get; set; } = true;
    public bool EnableGenericWebhook { get; set; } = true;

    /// <summary>
    /// Minimum time in seconds between alerts for the same channel.
    /// </summary>
    public double ThrottleCooldownSec { get; set; } = 15.0;

    /// <summary>
    /// Anomaly Z-Score jump required to bypass throttle cooldown.
    /// </summary>
    public double MinZScoreJumpForBypass { get; set; } = 1.5;
}
