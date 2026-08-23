using System;
using System.Windows.Threading;
using TelemetryDashboard.Core.Analytics;

namespace TelemetryDashboard.UI.Controls;

/// <summary>
/// Says, on this screen, whether the declared bands are judging anything.
/// </summary>
/// <remarks>
/// The sibling of the silence watch: that one asks whether a channel stopped reporting, this asks
/// whether anything is judging what does report. <see cref="LimitArmingWatch"/> holds the decision
/// and the account of why it is shaped the way it is; what is left here is a timer and a log line.
/// </remarks>
public partial class ControlPanelControl
{
    private readonly LimitArmingWatch _arming = new();
    private DispatcherTimer? _armingTimer;

    /// <summary>How long a band may stay unevaluated before it is worth saying so.</summary>
    public TimeSpan ArmingGrace
    {
        get => _arming.Grace;
        set => _arming.Grace = value;
    }

    /// <summary>Starts watching whether the declared bands ever judge anything.</summary>
    public void StartArmingWatch()
    {
        if (_armingTimer is not null) return;

        _armingTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _armingTimer.Tick += (_, _) => SweepForUnarmedLimits();
        _armingTimer.Start();
    }

    /// <summary>Forgets what was reported, for a profile change.</summary>
    public void ResetArmingWatch() => _arming.Reset(DateTime.UtcNow);

    /// <summary>Runs one arming check. Public so a caller can drive it without waiting on a clock.</summary>
    public void SweepForUnarmedLimits()
    {
        foreach (string line in _arming.Sweep(_limits?.Snapshot(), DateTime.UtcNow))
        {
            LogMessage("LIMIT", line);
        }
    }
}
