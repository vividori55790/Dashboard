using System;
using System.Collections.Generic;
using TelemetryDashboard.Core.Query;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// The per-channel families: what each channel last read, and what its history has cost.
/// </summary>
/// <remarks>
/// No unit suffix anywhere in here, and that is deliberate rather than an omission. The naming
/// conventions require base units in the name -- <c>_seconds</c>, <c>_bytes</c>, <c>_celsius</c> --
/// and the series store does not carry a channel's unit: it holds a name and a ring of doubles, and
/// the <c>unit</c> field on the wire is dropped before ingest reaches it. A suffix chosen here would
/// be a claim about the quantity that nothing measured, on the endpoint whose whole argument is
/// against exactly that. <c>telemetry_channel_value</c> claims a reading and nothing more.
/// <para>
/// <b>Product decision, not taken: how many channels belong on this endpoint.</b> Every channel the
/// store holds appears here, in five families, so a host at its 20,000-channel ceiling exposes on
/// the order of 100,000 series to every scraper. That is bounded rather than unbounded -- the store
/// refuses channels past the ceiling -- but it is a bill somebody else pays, in their storage and
/// their query latency, and the alternative is a configured subset. Exporting everything is the
/// honest default because the channel an operator forgot to list is the one that goes quiet, and
/// narrowing it is a choice about their monitoring system rather than about this one.
/// </para>
/// </remarks>
public static partial class MetricsEndpoint
{
    /// <summary>How many of a channel's own sample intervals may pass before it stops being current.</summary>
    /// <remarks>
    /// A bound the channel's own history sets rather than a constant somebody chose, for the reason
    /// ARCHITECTURE's worked example gives about forecast ranges: this hub carries 20 Hz converters
    /// and channels that speak once a minute, and any single number is wrong for one of them. A
    /// fixed five-minute cut would delete a healthy slow channel and read as an outage -- which is
    /// this product's failure pointing the other way.
    /// </remarks>
    public const double StaleIntervals = 10.0;

    /// <summary>The longest silence any channel is given, whatever its cadence suggests.</summary>
    /// <remarks>
    /// A ceiling, because the allowance above is derived from a history that may itself be ancient:
    /// a channel that spoke twice a day apart and then stopped would otherwise earn a twelve-hour
    /// tolerance and go on being exported as current. It is also the answer for a channel with a
    /// single sample, whose cadence is unknowable from one point.
    /// </remarks>
    public const double MaxStaleSec = 3600.0;

    /// <summary>One channel as it stood at one instant.</summary>
    /// <remarks>
    /// Read once and emitted five times rather than re-read per family. The families have to be
    /// written as five separate groups -- the format requires all lines for a metric to be
    /// contiguous -- and re-reading the ring for each would let a channel's exported value come
    /// from one sample while its exported timestamp came from a later one, which is precisely the
    /// pair a consumer subtracts to decide whether the reading is fresh.
    /// </remarks>
    private readonly record struct Reading(
        string Channel, double? Value, double NewestSec, long Appended, long Evicted, long OutOfOrder);

    private static void WriteChannels(Document document, TelemetryStreamingServer server, double nowSec)
    {
        var readings = new List<Reading>();
        var scratch = new SeriesPoint[1];

        foreach (string channel in server.Series.Channels)
        {
            if (IsThisHostsOwnVerdict(channel)) continue;
            if (Read(server.Series, channel, nowSec, scratch) is { } reading) readings.Add(reading);
        }

        // Ordinal, and only so the document is byte-stable between scrapes of an unchanged host.
        // The format imposes no order; a diff that is all reordering hides the one line that moved.
        readings.Sort((left, right) => string.CompareOrdinal(left.Channel, right.Channel));

        Family value = document.Open("channel_value", "gauge",
            "Most recent reading of a channel. Absent when the channel has no sample, and absent "
            + "once its newest sample is older than the channel's own cadence justifies -- an "
            + "outdated reading exported as current is a claim nobody measured.");

        foreach (Reading reading in readings)
        {
            if (reading.Value is { } current) SampleChannel(value, current, reading.Channel);
        }

        Family seen = document.Open("channel_last_sample_timestamp_seconds", "gauge",
            "When a channel last produced a sample, in seconds since the Unix epoch. Present for "
            + "every channel that has ever spoken, including ones now too stale to have a value, "
            + "so a scraper can subtract it from time() and see the silence for itself.");

        foreach (Reading reading in readings) SampleChannel(seen, reading.NewestSec, reading.Channel);

        Family appended = document.Open("channel_samples_total", "counter",
            "Samples this host has written for a channel since it started. Its rate is the "
            + "channel's real sample rate, which is the number that falls before a reading does.");

        foreach (Reading reading in readings) SampleChannel(appended, reading.Appended, reading.Channel);

        Family evicted = document.Open("channel_evicted_samples_total", "counter",
            "Samples overwritten by newer ones because the channel's ring was full. Non-zero means "
            + "history is being lost, and a query reaching further back than the ring holds will "
            + "return a window shorter than it asked for.");

        foreach (Reading reading in readings) SampleChannel(evicted, reading.Evicted, reading.Channel);

        Family disordered = document.Open("channel_out_of_order_samples_total", "counter",
            "Samples that arrived stamped earlier than their predecessor. Non-zero means the "
            + "channel's timeline is not monotonic, so a window query may include or exclude a "
            + "sample at its boundary.");

        foreach (Reading reading in readings) SampleChannel(disordered, reading.OutOfOrder, reading.Channel);
    }

    /// <summary>One channel's standing, or null when it has nothing to report.</summary>
    /// <remarks>
    /// Null rather than a zeroed row. A channel the store has no buffer for, or whose ring is
    /// empty, has produced nothing, and every counter below would be a truthful-looking zero for a
    /// series that does not exist.
    /// </remarks>
    private static Reading? Read(SeriesStore store, string channel, double nowSec, SeriesPoint[] scratch)
    {
        if (store.Find(channel) is not { } buffer) return null;
        if (buffer.NewestTimestampSec is not { } newest) return null;

        int count = buffer.Count;
        long evicted = buffer.EvictedSampleCount;

        double allowance = MeanIntervalSec(buffer, newest, count) is { } interval
            ? Math.Min(MaxStaleSec, StaleIntervals * interval)
            : MaxStaleSec;

        // The newest sample by arrival, which is what NewestTimestampSec reports. A channel with
        // out-of-order arrivals can hold a later stamp further back in the ring; the window search
        // is ordered and would miss it, and the copy then returns nothing. Absent is the answer
        // there too -- this endpoint does not guess which of two disordered samples is current.
        double? value = nowSec - newest <= allowance && buffer.CopyWindow(newest, newest, scratch) > 0
            ? scratch[0].Value
            : null;

        return new Reading(channel, value, newest, count + evicted, evicted, buffer.OutOfOrderArrivals);
    }

    /// <summary>The channel's mean sample interval, or null when it cannot be worked out.</summary>
    private static double? MeanIntervalSec(ChannelSeriesBuffer buffer, double newest, int count)
    {
        if (count < 2 || buffer.OldestTimestampSec is not { } oldest) return null;

        double span = newest - oldest;
        return span > 0.0 ? span / (count - 1) : null;
    }
}
