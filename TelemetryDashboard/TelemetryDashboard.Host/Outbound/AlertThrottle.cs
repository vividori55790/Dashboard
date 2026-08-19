using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Host.Outbound;

/// <summary>
/// Lets the first alert on a channel through and holds the rest back for a cooldown.
/// </summary>
/// <remarks>
/// An anomaly is a state, not an event. A channel that goes out of range stays out of range for
/// thousands of samples, so relaying every one of them would post thousands of Slack messages
/// about a single fault and get the workspace rate-limited exactly when the alerts matter. The
/// count of what was held back is kept and released with the next message, so the operator is told
/// the fault persisted rather than being left to assume it cleared after the first alert.
/// </remarks>
public sealed class AlertThrottle
{
    private readonly Dictionary<string, (DateTime LastSent, int Suppressed)> _channels = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly Func<DateTime> _now;

    /// <summary>Quiet period after an alert before the same channel may alert again.</summary>
    public TimeSpan Cooldown { get; }

    /// <param name="clock">Injected so the cooldown is testable without waiting for it.</param>
    public AlertThrottle(TimeSpan cooldown, Func<DateTime>? clock = null)
    {
        Cooldown = cooldown;
        _now = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Whether to send now, and how many alerts on this channel were held back since the last one.
    /// </summary>
    public bool ShouldSend(string channel, out int suppressedSinceLast)
    {
        DateTime now = _now();

        lock (_gate)
        {
            if (!_channels.TryGetValue(channel, out (DateTime LastSent, int Suppressed) state))
            {
                _channels[channel] = (now, 0);
                suppressedSinceLast = 0;
                return true;
            }

            if (now - state.LastSent < Cooldown)
            {
                _channels[channel] = (state.LastSent, state.Suppressed + 1);
                suppressedSinceLast = 0;
                return false;
            }

            _channels[channel] = (now, 0);
            suppressedSinceLast = state.Suppressed;
            return true;
        }
    }

    /// <summary>Alerts currently held back, across all channels.</summary>
    public int PendingSuppressed
    {
        get
        {
            lock (_gate)
            {
                int total = 0;
                foreach ((DateTime _, int suppressed) in _channels.Values) total += suppressed;
                return total;
            }
        }
    }
}
