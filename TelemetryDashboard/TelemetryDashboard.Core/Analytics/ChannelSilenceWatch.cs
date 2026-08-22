using System;
using System.Collections.Generic;
using System.Linq;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>What changed about a channel's silence on this sweep.</summary>
public enum SilenceTransition
{
    /// <summary>Nothing to report.</summary>
    None,

    /// <summary>The channel has just stopped reporting for longer than it ever normally does.</summary>
    WentSilent,

    /// <summary>It is reporting again.</summary>
    Returned
}

/// <summary>One channel's silence, as of a sweep.</summary>
public readonly record struct ChannelSilence(string Channel, double Seconds, SilenceTransition Transition);

/// <summary>
/// Notices a channel that has stopped reporting, which no value-watching detector can.
/// </summary>
/// <remarks>
/// A dead sensor looks exactly like a steady one. Every chart draws the last value it was given, so
/// a converter whose link drops holds its final reading on screen, inside its limits, with a
/// z-score of zero because the distribution stopped moving too. The failure is the absence of
/// values and nothing driven by values can see it.
/// <para>
/// The threshold is each channel's own cadence, not one number for the rig. A 20 Hz rail and a probe
/// reporting once a minute are both healthy and both quiet most of the time; a fixed five seconds
/// calls one dead and lets the other rot. So a channel is judged against the gap it has been
/// showing, times <see cref="Factor"/>, never below <see cref="MinimumSeconds"/>.
/// </para>
/// <para>
/// Deliberately not shared with the headless side's <c>ChannelIntervalProjection</c>, which asks the
/// same question and needs a different answer. That one publishes the current silence on every sweep
/// so the derived series climbs while a link is down; this one reports only the crossing, because an
/// alarm repeated once a second for as long as a cable stays out is an alarm that gets muted. One
/// type doing both would be a flag at every call site deciding which it meant.
/// </para>
/// </remarks>
public sealed class ChannelSilenceWatch
{
    /// <summary>
    /// How many of its own typical gaps a channel may miss before it counts as silent. A link that
    /// skips a frame under load is not a dead link, and an alarm that fires on one gets muted.
    /// </summary>
    public double Factor { get; init; } = 5.0;

    /// <summary>Silence below which nothing is ever called silent, whatever the cadence.</summary>
    public double MinimumSeconds { get; init; } = 5.0;

    private readonly record struct Timing(DateTimeOffset Seen, double Gap, bool Silent);

    private readonly Dictionary<string, Timing> _channels = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Channels seen at least once.</summary>
    public int TrackedChannels
    {
        get { lock (_channels) return _channels.Count; }
    }

    /// <summary>Channels currently counted as silent.</summary>
    public int SilentChannels
    {
        get { lock (_channels) return _channels.Values.Count(timing => timing.Silent); }
    }

    /// <summary>
    /// Records that a channel reported, and says whether that ends a silence. The recovery is
    /// answered here rather than on the next sweep, so a link that is back says so at once.
    /// </summary>
    public SilenceTransition Observe(string channel, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(channel)) return SilenceTransition.None;

        lock (_channels)
        {
            if (!_channels.TryGetValue(channel, out Timing previous))
            {
                _channels[channel] = new Timing(at, 0.0, false);
                return SilenceTransition.None;
            }

            double gap = (at - previous.Seen).TotalSeconds;

            // A gap that closed a silence is not this channel's cadence -- it is the length of the
            // outage. Keeping it would raise the bar for the next one by however long the last
            // fault lasted, so a channel that dropped for an hour would need another hour to be
            // called silent again.
            double cadence = previous.Silent || gap <= 0 ? previous.Gap : gap;

            _channels[channel] = new Timing(at, cadence, false);
            return previous.Silent ? SilenceTransition.Returned : SilenceTransition.None;
        }
    }

    /// <summary>Seconds a channel may be quiet before it counts as silent.</summary>
    public double ThresholdFor(string channel)
    {
        lock (_channels)
        {
            double gap = _channels.TryGetValue(channel, out Timing timing) ? timing.Gap : 0.0;
            return Math.Max(MinimumSeconds, gap * Factor);
        }
    }

    /// <summary>
    /// The channels that have just gone silent, as of <paramref name="now"/>. Only the crossing:
    /// a cable that has been out for an hour is one fault, not one alarm a second.
    /// </summary>
    public IReadOnlyList<ChannelSilence> Sweep(DateTimeOffset now)
    {
        var crossed = new List<ChannelSilence>();

        lock (_channels)
        {
            // Collected first, then written: overwriting a value mid-enumeration is allowed on
            // modern Dictionary and was not always, which is how a thing breaks on one runtime.
            var goneSilent = new List<string>();

            foreach ((string channel, Timing timing) in _channels)
            {
                if (timing.Silent) continue;

                double quiet = (now - timing.Seen).TotalSeconds;
                if (quiet < Math.Max(MinimumSeconds, timing.Gap * Factor)) continue;

                goneSilent.Add(channel);
                crossed.Add(new ChannelSilence(channel, quiet, SilenceTransition.WentSilent));
            }

            foreach (string channel in goneSilent)
            {
                _channels[channel] = _channels[channel] with { Silent = true };
            }
        }

        return crossed;
    }

    /// <summary>Forgets every channel, so a source change does not read as an outage.</summary>
    public void Reset()
    {
        lock (_channels) _channels.Clear();
    }
}
