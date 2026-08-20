using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Storage;

/// <summary>
/// Turns a batch of arriving packets into one compressed block per channel.
/// </summary>
/// <remarks>
/// Blocks are cut on batch boundaries rather than by a timer inside the store. Buffering samples
/// here to make bigger blocks would mean returning from a write before the data was durable, and
/// the ring feeding this store drops a packet the moment it is handed over — so a crash would lose
/// samples the pipeline had already been told were safe. Block size is therefore the caller's
/// batching decision, and it is worth making deliberately: compression per sample improves sharply
/// with run length, so grouping a channel's samples into one flush turns a modest saving into a
/// large one.
/// </remarks>
public static class TelemetryBlockBuilder
{
    /// <summary>
    /// Groups <paramref name="packets"/> by channel and encodes each group in timestamp order.
    /// </summary>
    /// <remarks>
    /// The sort is stable, so packets sharing a timestamp keep their arrival order — the same
    /// tie-break the raw SQLite path uses when it orders by tick then row id, and the reason a
    /// replayed trace does not reshuffle between runs.
    /// </remarks>
    /// <exception cref="ArgumentException">The sequence contains a null packet.</exception>
    public static IReadOnlyList<CompressedSampleBlock> Build(IEnumerable<TelemetryPacket> packets)
    {
        ArgumentNullException.ThrowIfNull(packets);

        var byChannel = new Dictionary<ChannelKey, List<TelemetryPacket>>();
        foreach (TelemetryPacket packet in packets)
        {
            if (packet is null)
            {
                throw new ArgumentException(
                    "A null packet cannot be encoded; the batch was not written.", nameof(packets));
            }

            ChannelKey key = ChannelKey.From(packet.NodeId, packet.Variable);
            if (!byChannel.TryGetValue(key, out List<TelemetryPacket>? group))
            {
                group = new List<TelemetryPacket>();
                byChannel[key] = group;
            }

            group.Add(packet);
        }

        var blocks = new List<CompressedSampleBlock>(byChannel.Count);
        foreach ((ChannelKey channel, List<TelemetryPacket> group) in byChannel)
        {
            blocks.Add(Encode(channel, group));
        }

        return blocks;
    }

    /// <summary>
    /// Encodes one channel's group. The unit is taken from the earliest sample in the block.
    /// </summary>
    /// <remarks>
    /// A channel that changes unit mid-block keeps the first sample's unit, which is a real if
    /// unusual loss. The alternative — a unit stream per sample — would spend space on every
    /// reading to record something that changes when firmware is reflashed, if ever.
    /// </remarks>
    private static CompressedSampleBlock Encode(ChannelKey channel, List<TelemetryPacket> group)
    {
        TelemetryPacket[] ordered = group
            .OrderBy(p => RollupIntervals.ToUtcTicks(p.Timestamp))
            .ToArray();

        long[] ticks = new long[ordered.Length];
        double[] values = new double[ordered.Length];
        long[] flags = new long[ordered.Length];
        for (int i = 0; i < ordered.Length; i++)
        {
            ticks[i] = RollupIntervals.ToUtcTicks(ordered[i].Timestamp);
            values[i] = ordered[i].Value;
            flags[i] = (long)ordered[i].Flags;
        }

        return new CompressedSampleBlock(
            channel,
            ordered[0].Unit ?? string.Empty,
            ticks[0],
            ticks[^1],
            ticks.Length,
            GorillaBlockCodec.Encode(ticks, values, flags));
    }
}
