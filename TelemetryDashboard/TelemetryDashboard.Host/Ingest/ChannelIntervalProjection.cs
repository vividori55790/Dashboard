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
/// Nothing new had to learn what staleness is.
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

    private readonly Dictionary<DataKey, DateTimeOffset> _lastSeen = new();
    private readonly Dictionary<DataKey, double> _lastReported = new();

    /// <summary>Channels that have reported at least once.</summary>
    public int TrackedChannels
    {
        get { lock (_lastSeen) return _lastSeen.Count; }
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
    /// watching for a link that has gone quiet.
    /// <para>
    /// A non-positive gap is refused for the same reason. Two records sharing a timestamp, or one
    /// arriving stamped earlier than its predecessor after a clock correction, describe no interval
    /// that elapsed; reporting the arithmetic would put a zero or a negative into a series whose
    /// entire purpose is to grow when nothing is arriving.
    /// </para>
    /// </remarks>
    public double? Measure(DataRecord record)
    {
        if (record is null) return null;

        lock (_lastSeen)
        {
            bool seen = _lastSeen.TryGetValue(record.Key, out DateTimeOffset previous);
            _lastSeen[record.Key] = record.Timestamp;

            if (!seen) return null;

            double seconds = (record.Timestamp - previous).TotalSeconds;
            if (seconds <= 0) return null;

            _lastReported[record.Key] = seconds;
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
    /// declared, and the alarm never fires. The absence of values was the failure being watched for,
    /// and watching it with something that is itself driven by values cannot work.
    /// <para>
    /// A channel is reported only once its silence exceeds the last gap it actually showed, so a
    /// link running normally produces nothing here at all — the sweep speaks when a channel is
    /// already late, and then keeps speaking so the series climbs while the link is down.
    /// </para>
    /// </remarks>
    public IReadOnlyList<DataRecord> Sweep(DateTimeOffset now, string source = "")
    {
        var overdue = new List<DataRecord>();

        lock (_lastSeen)
        {
            foreach ((DataKey key, DateTimeOffset seen) in _lastSeen)
            {
                double silent = (now - seen).TotalSeconds;
                if (silent <= 0) continue;

                double expected = _lastReported.TryGetValue(key, out double gap) ? gap : 0.0;
                if (silent <= expected) continue;

                overdue.Add(DataRecord.Derived(
                    key.Stream, key.Key + KeySuffix, new DataValue.Numeric(silent, "s"),
                    ProjectionName, now, source));
            }
        }

        return overdue;
    }

    /// <summary>Forgets every channel, so the next record starts a fresh series.</summary>
    /// <remarks>
    /// Called when the source changes. Carrying a timestamp across a reconnect would report the
    /// length of the outage as one interval on the first sample back — which is true, and is also
    /// the one reading guaranteed to breach whatever limit was set, at the moment the link
    /// recovered.
    /// </remarks>
    public void Reset()
    {
        lock (_lastSeen)
        {
            _lastSeen.Clear();
            _lastReported.Clear();
        }
    }
}
