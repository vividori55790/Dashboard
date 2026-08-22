using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>What changed about a channel's alarm state on this reading.</summary>
public enum AlarmTransition
{
    /// <summary>Nothing to report: the channel is quiet and was quiet.</summary>
    None,

    /// <summary>The channel just became alarming. This is the one worth interrupting someone for.</summary>
    Entered,

    /// <summary>Still alarming, and already announced.</summary>
    Sustained,

    /// <summary>Back inside the band, so an operator watching the banner can stop watching it.</summary>
    Cleared
}

/// <summary>
/// Decides when a scored channel is worth raising an alarm about, as opposed to worth drawing.
/// </summary>
/// <remarks>
/// Two separate things were being conflated. <see cref="TelemetryMlAnalyticsEngine.ZScoreThreshold"/>
/// is 2.5 sigma, which is a good bar for marking a point on a chart — roughly one sample in a
/// hundred of ordinary noise clears it. Used as an alarm bar at 20 Hz that is an interruption every
/// few seconds on a machine doing nothing wrong.
/// <para>
/// And even at a higher bar, alarming per sample is the wrong shape: a genuine excursion lasting
/// ten seconds is one event, not two hundred. This reports the <em>transition</em>, which is the
/// same distinction the limit monitor on the headless side already draws between entering a breach
/// and remaining in one.
/// </para>
/// <para>
/// The two thresholds are deliberately different. A channel sitting exactly on one bar would
/// otherwise alternate between alarming and clear on consecutive samples, and an operator would be
/// shown an alarm that raises and clears several times a second — which is worse than either state
/// held steadily, because it also destroys the log.
/// </para>
/// </remarks>
public sealed class AnomalyAlarmGate
{
    /// <summary>Sigma at or above which a channel starts alarming.</summary>
    /// <remarks>
    /// Higher than the engine's detection threshold on purpose. This is the bar for taking an
    /// operator's attention away from whatever they were doing, and it was the number the desktop
    /// already used before this class existed — repeated inline, in one of the two paths that
    /// scored a channel.
    /// </remarks>
    public double EnterSigma { get; init; } = 3.5;

    /// <summary>Sigma below which an alarming channel goes quiet again.</summary>
    public double ClearSigma { get; init; } = 2.5;

    /// <summary>
    /// How far a reading must sit from its own baseline, as a fraction of that baseline, before a
    /// sigma count is allowed to mean anything.
    /// </summary>
    /// <remarks>
    /// A z-score is a ratio, and a ratio with a very small denominator is very large for reasons
    /// that have nothing to do with the process. A channel that has been almost perfectly still
    /// has a standard deviation near zero, so the next ordinary wobble is dozens of sigma away from
    /// a mean it is in fact sitting right next to.
    /// <para>
    /// Measured on the running application before this existed: the ambient temperature raised an
    /// alarm at "23.98, 22.8 sigma" on an idle machine — while the engine's own readout for the
    /// same channel showed a mean of 23.7 and a standard deviation of 2.47, which puts that
    /// reading 0.11 sigma from the middle. Both numbers were right; they came from different
    /// moments, and the alarm came from the quiet one.
    /// </para>
    /// <para>
    /// This gate is for statistical anomalies — "this channel is behaving unlike itself". Absolute
    /// thresholds are a different mechanism with a different meaning, and this product already has
    /// one: limits declared per channel, in the channel's own unit, which do not care how still the
    /// signal has been.
    /// </para>
    /// </remarks>
    public double MinimumRelativeDeviation { get; init; } = 0.05;

    private readonly HashSet<string> _alarming = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Channels currently alarming.</summary>
    public int AlarmingCount => _alarming.Count;

    /// <summary>Whether this channel is currently alarming.</summary>
    public bool IsAlarming(string channel) => channel is not null && _alarming.Contains(channel);

    /// <summary>
    /// Folds one scored reading into the channel's alarm state.
    /// </summary>
    /// <remarks>
    /// A reading with no verdict is not evidence of calm. During warm-up the engine has no
    /// distribution to judge against, so it reports no verdict and a zero z-score — and treating
    /// that as "below the clear threshold" would silently clear an alarm that is still true every
    /// time the analyser was restarted.
    /// </remarks>
    public AlarmTransition Evaluate(string channel, AnomalyResult analysis)
    {
        if (string.IsNullOrWhiteSpace(channel) || analysis is null) return AlarmTransition.None;
        if (!analysis.HasVerdict) return _alarming.Contains(channel)
            ? AlarmTransition.Sustained
            : AlarmTransition.None;

        bool wasAlarming = _alarming.Contains(channel);

        // Absolute, unlike the engine's own IsAnomaly, which compares the signed z-score against
        // its threshold and so never flags a channel that has fallen. A rail dropping out is the
        // excursion an operator most wants to hear about.
        double sigma = Math.Abs(analysis.ZScore);

        double baseline = Math.Abs(analysis.Mean);
        double deviation = Math.Abs(analysis.CurrentValue - analysis.Mean);
        bool movedEnough = baseline > 0
            ? deviation / baseline >= MinimumRelativeDeviation
            : deviation > 0;

        if (!wasAlarming && sigma >= EnterSigma && movedEnough)
        {
            _alarming.Add(channel);
            return AlarmTransition.Entered;
        }

        if (wasAlarming && sigma < ClearSigma)
        {
            _alarming.Remove(channel);
            return AlarmTransition.Cleared;
        }

        return wasAlarming ? AlarmTransition.Sustained : AlarmTransition.None;
    }

    /// <summary>Forgets every channel's state, so the next reading can raise again.</summary>
    /// <remarks>Called when the source changes: a new rig's channels share nothing but their names.</remarks>
    public void Reset() => _alarming.Clear();
}
