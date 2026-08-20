using System;
using System.Collections.Generic;
using System.IO;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Storage;

/// <summary>
/// A run of one channel's samples, compressed into a single blob.
/// </summary>
/// <remarks>
/// The block is the unit of raw storage, and the reason this design can hold raw data at all at
/// scale: a row per sample pays a row header, a rowid, two index entries and a repeat of the node
/// and channel text on every single reading, while a block pays them once for the whole run.
/// <para>
/// A block carries timestamp, value and flags per sample and the unit once. It does not carry
/// <see cref="TelemetryPacket.RawData"/> — the original text line is the bulk of what makes a row
/// in the raw log cost a hundred bytes, and reproducing it is exactly what that log is for. A
/// caller that needs the wire text keeps the row store as well; a caller that does not gets the
/// measurement back bit-exact and pays a fraction of the space.
/// </para>
/// <para>
/// The time range sits beside the blob so a range query can skip a block without decompressing it,
/// and retention can decide a block's fate without opening it.
/// </para>
/// </remarks>
public sealed record CompressedSampleBlock(
    ChannelKey Channel,
    string Unit,
    long StartUtcTicks,
    long EndUtcTicks,
    int SampleCount,
    byte[] Payload)
{
    /// <summary>Timestamp of the first sample.</summary>
    public DateTime StartUtc => new(StartUtcTicks, DateTimeKind.Utc);

    /// <summary>Timestamp of the last sample. Inclusive.</summary>
    public DateTime EndUtc => new(EndUtcTicks, DateTimeKind.Utc);

    /// <summary>Compressed size of this block in bytes.</summary>
    public int PayloadBytes => Payload.Length;

    /// <summary>Whether this block holds any sample inside the given tick range.</summary>
    public bool Overlaps(long startTicks, long endTicks) =>
        EndUtcTicks >= startTicks && StartUtcTicks <= endTicks;

    /// <summary>
    /// Decodes the block into raw-tier points, in order.
    /// </summary>
    /// <remarks>
    /// Nothing is filtered: a NaN standing for "no reading" comes back as it was recorded, and
    /// deciding what to do with it belongs to the caller. The rollup tiers are where a NaN is
    /// excluded from an average.
    /// </remarks>
    /// <exception cref="InvalidDataException">The blob is not a readable block.</exception>
    public IReadOnlyList<TieredSeriesPoint> DecodePoints()
    {
        (long[] ticks, double[] values, _) = GorillaBlockCodec.Decode(Payload);

        var points = new List<TieredSeriesPoint>(ticks.Length);
        for (int i = 0; i < ticks.Length; i++)
        {
            points.Add(TieredSeriesPoint.FromSample(new DateTime(ticks[i], DateTimeKind.Utc), values[i]));
        }

        return points;
    }

    /// <summary>Decodes the block back into packets, restoring timestamp, value, flags and unit.</summary>
    /// <exception cref="InvalidDataException">The blob is not a readable block.</exception>
    public IReadOnlyList<TelemetryPacket> DecodePackets()
    {
        (long[] ticks, double[] values, long[] flags) = GorillaBlockCodec.Decode(Payload);

        var packets = new List<TelemetryPacket>(ticks.Length);
        for (int i = 0; i < ticks.Length; i++)
        {
            packets.Add(new TelemetryPacket
            {
                Timestamp = new DateTime(ticks[i], DateTimeKind.Utc),
                NodeId = Channel.NodeId,
                Variable = Channel.Variable,
                Value = values[i],
                Unit = Unit,
                RawData = string.Empty,
                Flags = (PacketFlags)flags[i]
            });
        }

        return packets;
    }
}
