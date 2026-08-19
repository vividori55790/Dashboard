using System;

namespace TelemetryDashboard.Core.Models;

/// <summary>
/// Operating mode for adaptive sampling state machine.
/// </summary>
public enum SamplingMode
{
    Nominal,
    Burst,
    Cooldown
}

/// <summary>
/// Event arguments for adaptive sampling frequency changes.
/// </summary>
public class SamplingRateChangedEventArgs : EventArgs
{
    public string ChannelId { get; set; } = string.Empty;
    public int OldRateHz { get; set; }
    public int NewRateHz { get; set; }
    public SamplingMode Mode { get; set; }
    public double TriggerZScore { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
