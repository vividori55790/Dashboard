using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Records;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// Turns "when did this channel last report" into a channel of its own.
/// </summary>
/// <remarks>
/// A dead sensor looks exactly like a steady one. Every chart in this product draws the last value
/// it was given, so a converter whose serial link drops mid-run holds its last reading on screen
/// indefinitely, inside its limits, with a z-score of zero because the distribution stopped moving
/// too. There is no value a value-watching alarm can fire on: the whole failure is the absence of
/// values.
/// <para>
/// The gap between reports is a number, though, and once it is a channel every mechanism this
/// product already has applies to it unchanged — a declared limit fires when a channel goes quiet
/// for longer than it should, and the rolling statistics flag a link whose jitter has grown.
/// </para>
/// <para>
/// Off by default. It roughly doubles the record count, which is a real cost on a busy rig, and an
/// operator should be the one deciding to pay it.
/// </para>
/// </remarks>
public sealed class ChannelIntervalProjection
{
    /// <summary>Suffix the derived channel carries, appended to the source key.</summary>
    public const string KeySuffix = ".interval";

    /// <summary>Name this projection stamps on its output, and answers to in the counters.</summary>
    public const string ProjectionName = "channel-interval";

    /// <summary>How often the sweep looks for channels that have gone overdue.</summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);

    /// <summary>What is remembered about one channel between records.</summary>
    /// <param name="Seen">When it last reported.</param>
    /// <param name="Gap">The last interval it showed, which is what "overdue" is measured against.</param>
    /// <param name="Source">
    /// The port it reported on. Per channel, not per host: the sweep first took one source from its
    /// caller — whichever port had spoken most recently — which is always right on one port and
    /// wrong on two exactly when it matters. COM4's cable comes out, COM3 keeps talking, and every
    /// record saying COM4 went quiet names COM3.
    /// </param>
    private readonly record struct ChannelTiming(DateTimeOffset Seen, double Gap, string Source);

    /// <summary>
    /// One entry per channel rather than three dictionaries keyed the same way, which would be
    /// three chances to disagree: a timestamp updated without its port, or a gap left behind by a
    /// reset that cleared only two of them.
    /// </summary>
    private readonly Dictionary<DataKey, ChannelTiming> _channels = new();

    /// <summary>Channels that have reported at least once.</summary>
    public int TrackedChannels
    {
        get { lock (_channels) return _channels.Count; }
    }

    /// <summary>Builds the pipeline stage, emitting through <paramref name="emit"/>.</summary>
    public DerivedNumericProjection Stage(Func<DataRecord, CancellationToken, ValueTask> emit) =>
        new(ProjectionName, DataValueKind.Numeric, Measure, KeySuffix, "s", emit);

    /// <summary>
    /// Seconds since this channel's previous record, or null when there is no previous one.
    /// </summary>
    /// <remarks>
    /// Null, not zero, for a channel's first sighting. Zero is a measurement — "these two arrived
    /// together" — and seeding every channel with one would put a false floor under any limit
    /// watching for a link that has gone quiet. A non-positive gap is refused for the same reason:
    /// two records sharing a timestamp, or one stamped earlier than its predecessor after a clock
    /// correction, describe no interval that elapsed.
    /// </remarks>
    public double? Measure(DataRecord record)
    {
        if (record is null) return null;

        lock (_channels)
        {
            bool seen = _channels.TryGetValue(record.Key, out ChannelTiming previous);
            string port = record.Source ?? string.Empty;

            if (!seen)
            {
                _channels[record.Key] = new ChannelTiming(record.Timestamp, 0.0, port);
                return null;
            }

            double seconds = (record.Timestamp - previous.Seen).TotalSeconds;
            if (seconds <= 0)
            {
                _channels[record.Key] = previous with { Seen = record.Timestamp, Source = port };
                return null;
            }

            _channels[record.Key] = new ChannelTiming(record.Timestamp, seconds, port);
            return seconds;
        }
    }

    /// <summary>
    /// The channels that are overdue, as records of how long they have now been silent.
    /// </summary>
    /// <remarks>
    /// Without this the feature does not do the thing it exists for, and the way it fails is quiet.
    /// A projection only speaks when a record arrives, so a channel that stops entirely stops
    /// producing intervals too: the last gap it published sits there, inside whatever limit was
    /// declared, and the alarm never fires. Watching for the absence of values with something that
    /// is itself driven by values cannot work.
    /// <para>
    /// A channel is reported only once its silence exceeds the last gap it actually showed, so a
    /// link running normally produces nothing here — the sweep speaks when a channel is already
    /// late, and keeps speaking so the series climbs while the link is down.
    /// </para>
    /// </remarks>
    public IReadOnlyList<DataRecord> Sweep(DateTimeOffset now)
    {
        var overdue = new List<DataRecord>();

        lock (_channels)
        {
            foreach ((DataKey key, ChannelTiming timing) in _channels)
            {
                double silent = (now - timing.Seen).TotalSeconds;
                if (silent <= 0 || silent <= timing.Gap) continue;

                overdue.Add(DataRecord.Derived(
                    key.Stream, key.Key + KeySuffix, new DataValue.Numeric(silent, "s"),
                    ProjectionName, now, timing.Source));
            }
        }

        return overdue;
    }

    /// <summary>Forgets every channel, so the next record starts a fresh series.</summary>
    /// <remarks>
    /// Called when the source changes. Carrying a timestamp across a reconnect would report the
    /// length of the outage as one interval on the first sample back — true, and the one reading
    /// guaranteed to breach whatever limit was set, at the moment the link recovered.
    /// </remarks>
    public void Reset()
    {
        lock (_channels) _channels.Clear();
    }
}
