using System;

namespace TelemetryDashboard.Core.Models;

/// <summary>
/// Definition of an emergency rule evaluated against telemetry channels.
/// </summary>
public class EmergencyRule
{
    public string ChannelName { get; set; } = "*";
    public double ZScoreThreshold { get; set; } = 3.5;
    public double AbsoluteUpperLimit { get; set; } = double.MaxValue;
    public double AbsoluteLowerLimit { get; set; } = double.MinValue;
    public string CommandTxPayload { get; set; } = "$CMD,SAFE_MODE\n";
    public bool AutoExecute { get; set; } = true;
    public double CooldownSec { get; set; } = 10.0;
    public string TargetPort { get; set; } = "COM3";
}

/// <summary>
/// Historical record of a triggered emergency event.
/// </summary>
public class EmergencyEventRecord
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string ChannelName { get; set; } = string.Empty;
    public string TargetPort { get; set; } = string.Empty;
    public double ZScore { get; set; }
    public double Value { get; set; }
    public string CommandPayload { get; set; } = string.Empty;
    public bool Dispatched { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Event arguments for emergency trigger firing.
/// </summary>
public class EmergencyTriggerEventArgs : EventArgs
{
    public string ChannelName { get; set; } = string.Empty;
    public string PortOrNode { get; set; } = string.Empty;
    public double ZScore { get; set; }
    public double Value { get; set; }
    public string CommandTxPayload { get; set; } = string.Empty;
    public bool Dispatched { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
