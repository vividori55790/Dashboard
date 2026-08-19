using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Services;

/// <summary>
/// Dedicated Emergency MCU Controller.
/// Evaluates emergency condition thresholds (Z-Score > 3.5 sigma or absolute limits),
/// enforces safety arming/disarming interlocks, and dispatches emergency commands to MCU nodes.
/// </summary>
public class EmergencyMcuController : IEmergencyMcuController
{
    private readonly ISerialManager? _serialManager;
    private readonly Func<string, string, Task>? _dispatchCallback;
    private readonly List<EmergencyRule> _rules = new();
    private readonly List<EmergencyEventRecord> _history = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastDispatches = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public bool IsArmed { get; private set; } = true;

    public IReadOnlyList<EmergencyRule> Rules
    {
        get
        {
            lock (_lock) return _rules.ToList().AsReadOnly();
        }
    }

    public IReadOnlyList<EmergencyEventRecord> History
    {
        get
        {
            lock (_lock) return _history.ToList().AsReadOnly();
        }
    }

    public event EventHandler<EmergencyTriggerEventArgs>? EmergencyTriggered;

    public EmergencyMcuController(ISerialManager? serialManager = null, Func<string, string, Task>? dispatchCallback = null)
    {
        _serialManager = serialManager;
        _dispatchCallback = dispatchCallback;

        // Default Rule for general anomalies exceeding 3.5 sigma
        RegisterRule(new EmergencyRule
        {
            ChannelName = "*",
            ZScoreThreshold = 3.5,
            CommandTxPayload = "$CMD,SAFE_MODE\n",
            AutoExecute = true,
            CooldownSec = 5.0,
            TargetPort = "COM3"
        });
    }

    public void Arm()
    {
        IsArmed = true;
    }

    public void Disarm()
    {
        IsArmed = false;
    }

    public void RegisterRule(EmergencyRule rule)
    {
        lock (_lock)
        {
            _rules.Add(rule);
        }
    }

    public void ClearRules()
    {
        lock (_lock)
        {
            _rules.Clear();
        }
    }

    public bool EvaluateEmergencyTriggers(string channelName, double zScore, double value, out string txCommand)
    {
        txCommand = string.Empty;
        lock (_lock)
        {
            var matchingRule = _rules.FirstOrDefault(r =>
                (r.ChannelName == "*" || string.Equals(r.ChannelName, channelName, StringComparison.OrdinalIgnoreCase)) &&
                (Math.Abs(zScore) >= r.ZScoreThreshold || value >= r.AbsoluteUpperLimit || value <= r.AbsoluteLowerLimit) &&
                r.AutoExecute);

            if (matchingRule != null)
            {
                txCommand = matchingRule.CommandTxPayload;
                return true;
            }
        }

        return false;
    }

    public async Task<bool> EvaluateAndDispatchAsync(string port, string channelName, double zScore, double value, CancellationToken cancellationToken = default)
    {
        EmergencyRule? matchingRule = null;
        lock (_lock)
        {
            matchingRule = _rules.FirstOrDefault(r =>
                (r.ChannelName == "*" || string.Equals(r.ChannelName, channelName, StringComparison.OrdinalIgnoreCase)) &&
                (Math.Abs(zScore) >= r.ZScoreThreshold || value >= r.AbsoluteUpperLimit || value <= r.AbsoluteLowerLimit) &&
                r.AutoExecute);
        }

        if (matchingRule == null)
        {
            return false;
        }

        string targetPort = !string.IsNullOrWhiteSpace(port) ? port : matchingRule.TargetPort;
        string throttleKey = $"{targetPort}:{channelName}";

        // Check Safety Interlock
        if (!IsArmed)
        {
            var disarmedRecord = new EmergencyEventRecord
            {
                Timestamp = DateTime.UtcNow,
                ChannelName = channelName,
                TargetPort = targetPort,
                ZScore = zScore,
                Value = value,
                CommandPayload = matchingRule.CommandTxPayload,
                Dispatched = false,
                Reason = "Suppressed: Controller is Disarmed"
            };

            lock (_lock)
            {
                _history.Add(disarmedRecord);
            }

            EmergencyTriggered?.Invoke(this, new EmergencyTriggerEventArgs
            {
                ChannelName = channelName,
                PortOrNode = targetPort,
                ZScore = zScore,
                Value = value,
                CommandTxPayload = matchingRule.CommandTxPayload,
                Dispatched = false,
                Timestamp = disarmedRecord.Timestamp
            });

            return false;
        }

        // Check Debounce Cooldown
        if (matchingRule.CooldownSec > 0 && _lastDispatches.TryGetValue(throttleKey, out var lastTime))
        {
            if ((DateTime.UtcNow - lastTime).TotalSeconds < matchingRule.CooldownSec)
            {
                return false; // Suppressed by debounce cooldown
            }
        }

        _lastDispatches[throttleKey] = DateTime.UtcNow;

        // Dispatch emergency command
        if (_dispatchCallback != null)
        {
            await _dispatchCallback(targetPort, matchingRule.CommandTxPayload);
        }
        else if (_serialManager != null)
        {
            await _serialManager.WriteLineAsync(targetPort, matchingRule.CommandTxPayload, cancellationToken);
        }

        var dispatchedRecord = new EmergencyEventRecord
        {
            Timestamp = DateTime.UtcNow,
            ChannelName = channelName,
            TargetPort = targetPort,
            ZScore = zScore,
            Value = value,
            CommandPayload = matchingRule.CommandTxPayload,
            Dispatched = true,
            Reason = "Auto-Execute Emergency Condition Met"
        };

        lock (_lock)
        {
            _history.Add(dispatchedRecord);
        }

        EmergencyTriggered?.Invoke(this, new EmergencyTriggerEventArgs
        {
            ChannelName = channelName,
            PortOrNode = targetPort,
            ZScore = zScore,
            Value = value,
            CommandTxPayload = matchingRule.CommandTxPayload,
            Dispatched = true,
            Timestamp = dispatchedRecord.Timestamp
        });

        return true;
    }

    public async Task<int> EmergencyStopAllAsync(string reason = "Manual Emergency Stop", CancellationToken cancellationToken = default)
    {
        const string stopCommand = "$CMD,EMERGENCY_STOP\n";
        int count = 0;

        if (_serialManager != null && _serialManager.ActivePorts.Count > 0)
        {
            foreach (var port in _serialManager.ActivePorts.Keys)
            {
                await _serialManager.WriteLineAsync(port, stopCommand, cancellationToken);
                count++;
            }
        }
        else if (_dispatchCallback != null)
        {
            await _dispatchCallback("ALL", stopCommand);
            count = 1;
        }

        var stopRecord = new EmergencyEventRecord
        {
            Timestamp = DateTime.UtcNow,
            ChannelName = "ALL",
            TargetPort = "ALL",
            ZScore = double.NaN,
            Value = double.NaN,
            CommandPayload = stopCommand,
            Dispatched = true,
            Reason = reason
        };

        lock (_lock)
        {
            _history.Add(stopRecord);
        }

        EmergencyTriggered?.Invoke(this, new EmergencyTriggerEventArgs
        {
            ChannelName = "ALL",
            PortOrNode = "ALL",
            ZScore = 0,
            Value = 0,
            CommandTxPayload = stopCommand,
            Dispatched = true,
            Timestamp = stopRecord.Timestamp
        });

        return count;
    }

    public void AcknowledgeEmergency(string channelName)
    {
        if (string.Equals(channelName, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            _lastDispatches.Clear();
        }
        else
        {
            var keysToRemove = _lastDispatches.Keys.Where(k => k.EndsWith($":{channelName}", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var key in keysToRemove)
            {
                _lastDispatches.TryRemove(key, out _);
            }
        }
    }
}
