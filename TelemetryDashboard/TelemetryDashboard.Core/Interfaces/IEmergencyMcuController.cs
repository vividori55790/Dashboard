using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Interfaces;

/// <summary>
/// Interface for Conditional Emergency MCU Controller.
/// Evaluates severe statistical anomalies (Z-Score > 3.5 sigma) and absolute boundary breaches,
/// enforces safety arming/disarming interlocks, and dispatches emergency commands to hardware serial ports.
/// </summary>
public interface IEmergencyMcuController
{
    bool IsArmed { get; }
    void Arm();
    void Disarm();

    IReadOnlyList<EmergencyRule> Rules { get; }
    IReadOnlyList<EmergencyEventRecord> History { get; }

    void RegisterRule(EmergencyRule rule);
    void ClearRules();

    bool EvaluateEmergencyTriggers(string channelName, double zScore, double value, out string txCommand);
    Task<bool> EvaluateAndDispatchAsync(string port, string channelName, double zScore, double value, CancellationToken cancellationToken = default);
    Task<int> EmergencyStopAllAsync(string reason = "Manual Emergency Stop", CancellationToken cancellationToken = default);
    void AcknowledgeEmergency(string channelName);

    event EventHandler<EmergencyTriggerEventArgs>? EmergencyTriggered;
}
