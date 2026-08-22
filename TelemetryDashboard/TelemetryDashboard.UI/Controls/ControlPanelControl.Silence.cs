using System;
using System.Globalization;
using System.Windows.Threading;
using TelemetryDashboard.Core.Analytics;

namespace TelemetryDashboard.UI.Controls;

/// <summary>
/// Noticing, on the desktop, that a channel has stopped reporting.
/// </summary>
/// <remarks>
/// The headless host has watched for this since <c>--watch-intervals</c>, and the shell — the half
/// most people actually run — could not see it at all. A dead sensor looks exactly like a steady
/// one here: the scope draws the last value it was given, the statistics readout holds the mean it
/// last computed, and the z-score sits at zero because the distribution stopped moving too. Every
/// surface on the panel agrees that everything is fine.
/// <para>
/// The alarm surface was already here — <see cref="AlertRaised"/> puts a banner up and speaks it —
/// and so was the observation point: <c>UpdateChannelStats</c> is called for every sample on both
/// the simulated and the hardware paths. What was missing was anything watching the clock.
/// </para>
/// </remarks>
public partial class ControlPanelControl
{
    /// <summary>How often the panel looks for channels that have gone quiet.</summary>
    /// <remarks>
    /// A second. The watch decides <em>whether</em> a channel is late against its own cadence; this
    /// only decides how promptly the answer is noticed, and a slower tick would delay an alarm by
    /// more than it saves.
    /// </remarks>
    public static readonly TimeSpan SilenceSweepInterval = TimeSpan.FromSeconds(1);

    private readonly ChannelSilenceWatch _silence = new();
    private DispatcherTimer? _silenceTimer;

    /// <summary>The watch, so a caller can ask what it is tracking.</summary>
    public ChannelSilenceWatch Silence => _silence;

    /// <summary>Starts the sweep. Idempotent, so re-attaching a source does not stack timers.</summary>
    public void StartSilenceWatch()
    {
        if (_silenceTimer is not null) return;

        _silenceTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = SilenceSweepInterval };
        _silenceTimer.Tick += (_, _) => SweepForSilence();
        _silenceTimer.Start();
    }

    /// <summary>Forgets every channel, for a source change.</summary>
    /// <remarks>
    /// A watch carried across a source change reports the gap between two rigs as an outage in the
    /// first one, at the moment the operator switched deliberately.
    /// </remarks>
    public void ResetSilenceWatch() => _silence.Reset();

    /// <summary>Records that a channel reported, and says so when that ends an outage.</summary>
    private void ObserveForSilence(string channel, DateTime timestampUtc)
    {
        if (_silence.Observe(channel, new DateTimeOffset(DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc)))
            != SilenceTransition.Returned)
        {
            return;
        }

        // Logged rather than raised, on the same reasoning as a cleared anomaly: the operator needs
        // to know the link is back, and a banner for "the problem stopped" is an interruption
        // carrying no action.
        LogMessage("LINK", $"Reporting again: {channel}");
    }

    private void SweepForSilence()
    {
        foreach (ChannelSilence gone in _silence.Sweep(DateTimeOffset.UtcNow))
        {
            string message = string.Create(CultureInfo.InvariantCulture,
                $"{gone.Channel} has stopped reporting ({gone.Seconds:F0} s)");

            LogMessage("LINK", message);

            // Critical, so the banner stays until it is dismissed. A channel that has gone away is
            // not a reading an operator can glance at and judge -- it is the absence of one, and it
            // will not correct itself on the next sample the way an excursion might.
            AlertRaised?.Invoke(message, true);
        }
    }
}
