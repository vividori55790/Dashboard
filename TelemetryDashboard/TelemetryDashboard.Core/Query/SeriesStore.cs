using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace TelemetryDashboard.Core.Query;

/// <summary>
/// Rolling per-channel history, written on the ingest path and read by the query API.
/// </summary>
/// <remarks>
/// <para>
/// Ingest writes a pair of doubles into a ring; it does not serialise, format or fan anything out.
/// That is the point at a million samples a second: the cost of a sample arriving must not scale
/// with the number of browsers connected, and the cost of a browser connecting must not scale with
/// the sample rate.
/// </para>
/// <para>
/// The channel count is bounded. A million distinct channels at the default depth would be tens of
/// gigabytes, so the store refuses new channels past <see cref="MaxChannels"/> and counts the
/// refusals rather than silently retaining an arbitrary subset — a dashboard must be able to find
/// out that the channel it is plotting was never admitted.
/// </para>
/// </remarks>
public sealed class SeriesStore
{
    private readonly ConcurrentDictionary<string, ChannelSeriesBuffer> _channels =
        new(StringComparer.Ordinal);

    private long _samplesAccepted;
    private long _samplesRefused;

    public SeriesStore(int samplesPerChannel = 4096, int maxChannels = 20_000)
    {
        if (samplesPerChannel < 2) throw new ArgumentOutOfRangeException(nameof(samplesPerChannel));
        if (maxChannels < 1) throw new ArgumentOutOfRangeException(nameof(maxChannels));
        SamplesPerChannel = samplesPerChannel;
        MaxChannels = maxChannels;
    }

    /// <summary>Ring depth held for each channel.</summary>
    public int SamplesPerChannel { get; }

    /// <summary>Channels the store will admit before refusing new ones.</summary>
    public int MaxChannels { get; }

    public int ChannelCount => _channels.Count;

    /// <summary>Samples written into a ring.</summary>
    public long SamplesAccepted => Interlocked.Read(ref _samplesAccepted);

    /// <summary>
    /// Samples dropped because their channel could not be admitted.
    /// </summary>
    /// <remarks>
    /// Reported rather than hidden: a non-zero value means some channel is not queryable at all,
    /// and a chart of it would be blank for a reason that has nothing to do with the sensor.
    /// </remarks>
    public long SamplesRefused => Interlocked.Read(ref _samplesRefused);

    /// <summary>Every channel currently retained.</summary>
    public IReadOnlyCollection<string> Channels => System.Linq.Enumerable.ToArray(_channels.Keys);

    /// <summary>Records one sample against its channel.</summary>
    public void Append(string channel, double value, double timestampSec)
    {
        if (string.IsNullOrEmpty(channel)) return;

        if (!_channels.TryGetValue(channel, out ChannelSeriesBuffer? buffer))
        {
            if (_channels.Count >= MaxChannels)
            {
                Interlocked.Increment(ref _samplesRefused);
                return;
            }
            buffer = _channels.GetOrAdd(channel, _ => new ChannelSeriesBuffer(SamplesPerChannel));
        }

        buffer.Append(timestampSec, value);
        Interlocked.Increment(ref _samplesAccepted);
    }

    /// <summary>The buffer for a channel, or <c>null</c> when nothing has been recorded for it.</summary>
    public ChannelSeriesBuffer? Find(string channel) =>
        channel is not null && _channels.TryGetValue(channel, out ChannelSeriesBuffer? buffer) ? buffer : null;

    /// <summary>Removes every channel and its history.</summary>
    public void Clear() => _channels.Clear();
}
