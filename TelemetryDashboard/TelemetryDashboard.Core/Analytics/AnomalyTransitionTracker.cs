using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>Whether a channel just entered an anomalous state, left one, or neither.</summary>
public enum AnomalyTransition
{
    None,
    Entered,
    Cleared
}

/// <summary>
/// Turns a per-sample verdict into the two events worth telling somebody about.
/// </summary>
/// <remarks>
/// Edge detection alone is not enough, and the shell had exactly that: a set of channels currently
/// anomalous, an entry logged when a channel joined it and a recovery when it left. Its own note
/// promised "a channel that stays out of range for a minute is one event, not two thousand four
/// hundred".
/// <para>
/// It is not, because the verdict it watches is a bare threshold comparison. A reading hovering
/// near the bar crosses it in both directions every few samples, and every crossing is a genuine
/// edge. Measured on the running shell, one channel in four hundred milliseconds:
/// </para>
/// <code>
/// 02.675  anomalous  (z=2.78)
/// 02.860  recovered  (z=2.40)
/// 02.904  anomalous  (z=2.59)
/// 03.061  recovered  (z=2.30)
/// </code>
/// <para>
/// Four events, no change in the machine. At that rate the event log — three hundred rows, and the
/// place the silence watch, the limit alarms, the arming check and the link events all deliver
/// their answers — is emptied of everything else within seconds.
/// </para>
/// <para>
/// So a recovery has to be quiet for a while before it counts. The onset does not: an alarm that
/// waits before announcing itself is an alarm that arrives late, and the cost of the two mistakes
/// is not the same. The threshold itself is left to the detector, which owns it; this only decides
/// when a verdict is worth interrupting somebody with.
/// </para>
/// </remarks>
public sealed class AnomalyTransitionTracker
{
    /// <summary>How long a channel must read normally before its recovery is announced.</summary>
    /// <remarks>
    /// Long enough to outlast the flapping measured above, short enough that an operator watching a
    /// rail settle is not left wondering whether the alarm is stuck.
    /// </remarks>
    public static readonly TimeSpan DefaultCalmBeforeClear = TimeSpan.FromSeconds(5);

    private sealed class State
    {
        public bool InAnomaly;

        /// <summary>When this channel was last judged anomalous.</summary>
        /// <remarks>
        /// The wait is measured from here rather than from the first normal reading after it, and
        /// the difference only shows on sparse data -- where it decides the case. A channel that
        /// reports once a minute would otherwise need two normal readings to clear, so its recovery
        /// would arrive a minute after the machine recovered. "Nothing has been wrong for five
        /// seconds" is the question; the first calm sample is not the answer to it.
        /// </remarks>
        public DateTime LastAnomalyUtc;
    }

    private readonly Dictionary<string, State> _channels = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public TimeSpan CalmBeforeClear { get; set; } = DefaultCalmBeforeClear;

    /// <summary>Channels currently held in an announced anomaly.</summary>
    public int InAnomalyCount
    {
        get { lock (_gate) { int n = 0; foreach (State s in _channels.Values) if (s.InAnomaly) n++; return n; } }
    }

    /// <summary>Records one verdict and says whether it is worth a line.</summary>
    public AnomalyTransition Observe(string channel, bool isAnomaly, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrEmpty(channel);

        lock (_gate)
        {
            if (!_channels.TryGetValue(channel, out State? state))
            {
                state = new State();
                _channels[channel] = state;
            }

            if (isAnomaly)
            {
                // Any anomalous sample restarts the wait, so a channel that dips normal for one
                // reading in the middle of an excursion does not get half way to a recovery.
                state.LastAnomalyUtc = nowUtc;

                if (state.InAnomaly) return AnomalyTransition.None;

                state.InAnomaly = true;
                return AnomalyTransition.Entered;
            }

            if (!state.InAnomaly) return AnomalyTransition.None;
            if (nowUtc - state.LastAnomalyUtc < CalmBeforeClear) return AnomalyTransition.None;

            state.InAnomaly = false;
            return AnomalyTransition.Cleared;
        }
    }

    /// <summary>Forgets every channel, for a source or profile change.</summary>
    /// <remarks>
    /// State carried across a source change would announce a recovery for a rig nobody is watching
    /// any more, at the moment somebody switched deliberately.
    /// </remarks>
    public void Reset()
    {
        lock (_gate) _channels.Clear();
    }
}
