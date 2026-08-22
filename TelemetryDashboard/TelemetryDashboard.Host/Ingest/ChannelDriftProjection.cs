using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Records;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// Publishes each channel's slow drift as a channel of its own.
/// </summary>
/// <remarks>
/// Same shape as <see cref="ChannelIntervalProjection"/> and for the same reason: once drift is a
/// channel, every mechanism this product already has applies to it unchanged. A declared limit
/// fires when a rail has wandered further from where it lives than it should have, and the figure
/// is charted, recorded and archived beside the reading it came from.
/// <para>
/// It is the one detector here that can see a fault which never trips a threshold. A z-score
/// measures a reading against the window it just came from, so anything slow enough drags its own
/// baseline along and never scores — see <see cref="DriftMonitor"/> for what that misses and, just
/// as importantly, for what this does not claim to measure.
/// </para>
/// </remarks>
public sealed class ChannelDriftProjection
{
    /// <summary>Suffix the derived channel carries, appended to the source key.</summary>
    public const string KeySuffix = ".drift";

    /// <summary>Name this projection stamps on its output, and answers to in the counters.</summary>
    public const string ProjectionName = "channel-drift";

    private sealed record Tracked(DriftMonitor Monitor, string Unit)
    {
        public DateTimeOffset LastSeen { get; set; }
        public string Source { get; set; } = string.Empty;
    }

    /// <summary>How many times longer the long memory is than the short one.</summary>
    /// <remarks>
    /// Derived rather than configured, because the two are not independent. The first version took
    /// the long memory from the operator and left the short one at a fixed thirty seconds, so any
    /// window below a couple of minutes gave two averages that tracked each other and a difference
    /// that was noise -- exactly the failure <see cref="DriftMonitor.SlowSeconds"/> warns about, in
    /// the code that was supposed to honour it. Measured on a live host: at a 40-second window,
    /// nothing was ever published at all.
    /// </remarks>
    public const double MemoryRatio = 30.0;

    private readonly Dictionary<DataKey, Tracked> _channels = new();
    private readonly double _fastSeconds;
    private readonly double _slowSeconds;
    private readonly double _warmUpSeconds;

    /// <summary>Builds the projection around the long memory a drift figure is measured over.</summary>
    /// <param name="slowSeconds">
    /// How far back "where this channel has been living" reaches. Everything else follows from it.
    /// </param>
    /// <remarks>
    /// The warm-up is the long memory itself. A baseline reaching back fifteen minutes cannot be
    /// known in less than fifteen minutes, and offering a figure before then would be reporting the
    /// difference between a half-formed average and the reading that seeded it.
    /// </remarks>
    public ChannelDriftProjection(double slowSeconds = 900.0)
    {
        _slowSeconds = Math.Max(slowSeconds, 1.0);
        _fastSeconds = Math.Max(_slowSeconds / MemoryRatio, 0.1);
        _warmUpSeconds = _slowSeconds;
    }

    /// <summary>The short memory this window implies, in seconds.</summary>
    public double FastSeconds => _fastSeconds;

    /// <summary>The long memory, in seconds.</summary>
    public double SlowSeconds => _slowSeconds;

    /// <summary>Channels being tracked.</summary>
    public int TrackedChannels
    {
        get { lock (_channels) return _channels.Count; }
    }

    /// <summary>Builds the pipeline stage, emitting through <paramref name="emit"/>.</summary>
    public DerivedNumericProjection Stage(Func<DataRecord, CancellationToken, ValueTask> emit) =>
        new(ProjectionName, DataValueKind.Numeric, Measure, KeySuffix, string.Empty, emit,
            unitOf: record => UnitOf(record.Key));

    /// <summary>
    /// Drift for this channel, or null while it is warming up or the record is not one to measure.
    /// </summary>
    /// <remarks>
    /// A record another projection derived is skipped. Drift on an interval channel is arithmetic
    /// about the link's timing rather than about the plant, and with <c>--watch-intervals</c> also
    /// on it would double an already doubled record count for a figure nobody asked for.
    /// </remarks>
    public double? Measure(DataRecord record)
    {
        if (record is null || record.IsDerived || record.Value is not DataValue.Numeric numeric) return null;

        lock (_channels)
        {
            if (!_channels.TryGetValue(record.Key, out Tracked? tracked))
            {
                tracked = new Tracked(
                    new DriftMonitor
                    {
                        FastSeconds = _fastSeconds,
                        SlowSeconds = _slowSeconds,
                        WarmUpSeconds = _warmUpSeconds
                    },
                    numeric.Unit ?? string.Empty)
                { LastSeen = record.Timestamp, Source = record.Source ?? string.Empty };

                _channels[record.Key] = tracked;
                tracked.Monitor.Update(numeric.Value, 0);   // seeds both averages
                return null;
            }

            double elapsed = (record.Timestamp - tracked.LastSeen).TotalSeconds;
            tracked.LastSeen = record.Timestamp;
            tracked.Source = record.Source ?? string.Empty;

            return tracked.Monitor.Update(numeric.Value, elapsed);
        }
    }

    /// <summary>The unit a channel's drift is expressed in: the channel's own.</summary>
    /// <remarks>
    /// Drift is a difference between two averages of the same quantity, so it carries that
    /// quantity's unit — volts of drift on a voltage. Publishing it unitless would leave an operator
    /// writing a limit against a number whose scale they had to guess.
    /// </remarks>
    public string UnitOf(DataKey key)
    {
        lock (_channels) return _channels.TryGetValue(key, out Tracked? tracked) ? tracked.Unit : string.Empty;
    }

    /// <summary>Forgets every channel, so the next sample starts a fresh baseline.</summary>
    public void Reset()
    {
        lock (_channels) _channels.Clear();
    }
}
