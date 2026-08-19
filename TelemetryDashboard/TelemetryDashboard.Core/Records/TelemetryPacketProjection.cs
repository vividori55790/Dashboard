using System;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Records;

/// <summary>
/// Converts between <see cref="DataRecord"/> and the numeric <see cref="TelemetryPacket"/> the
/// existing pipeline runs on.
/// </summary>
/// <remarks>
/// This is the whole M6 bet in one file. Rather than widening every stage to accept any value,
/// a record whose value is <see cref="DataValue.Numeric"/> is projected onto the packet type the
/// compressor, the analytics engine, the DVR and the scope already handle — so a new domain
/// inherits ten years of numeric machinery by satisfying one shape, and a domain that cannot
/// satisfy it is refused here rather than silently mishandled four layers down.
/// </remarks>
public static class TelemetryPacketProjection
{
    /// <summary>
    /// Projects a record onto a packet, or returns false when the value is not numeric.
    /// </summary>
    /// <remarks>
    /// Returning false rather than substituting 0.0 is the point: a text or blob record has no
    /// magnitude, and inventing one would put a fabricated sample on a chart an operator trusts.
    /// </remarks>
    public static bool TryToPacket(DataRecord record, out TelemetryPacket packet)
    {
        packet = default!;
        if (record?.Value is not DataValue.Numeric numeric) return false;

        PacketFlags flags = PacketFlags.None;
        if (record.IsDerived) flags |= PacketFlags.IsDerived;

        packet = new TelemetryPacket
        {
            Timestamp = record.Timestamp.UtcDateTime,
            NodeId = record.Key.Stream,
            Variable = record.Key.Key,
            Value = numeric.Value,
            Unit = numeric.Unit,
            RawData = record.RawSource ?? string.Empty,
            Flags = flags
        };
        return true;
    }

    /// <summary>Lifts a packet onto the universal path.</summary>
    /// <param name="derivedFrom">
    /// Supplied when the packet is known to be computed. A packet carrying
    /// <see cref="PacketFlags.IsDerived"/> without a name becomes <see cref="UnnamedProjection"/>:
    /// the flag says it was derived, so the record must not claim it was measured, but the
    /// producer is genuinely unrecoverable at this point.
    /// </param>
    public static DataRecord ToRecord(TelemetryPacket packet, string? derivedFrom = null)
    {
        ArgumentNullException.ThrowIfNull(packet);

        string? producer = derivedFrom;
        if (producer is null && packet.Flags.HasFlag(PacketFlags.IsDerived))
        {
            producer = UnnamedProjection;
        }

        return new DataRecord
        {
            Key = new DataKey(packet.NodeId ?? string.Empty, packet.Variable ?? string.Empty),
            Timestamp = new DateTimeOffset(ToUtc(packet.Timestamp)),
            Value = new DataValue.Numeric(packet.Value, packet.Unit ?? string.Empty),
            Source = packet.NodeId ?? string.Empty,
            DerivedFrom = producer,
            RawSource = string.IsNullOrEmpty(packet.RawData) ? null : packet.RawData
        };
    }

    /// <summary>Marks a record known to be derived whose producing projection was not recorded.</summary>
    public const string UnnamedProjection = "unnamed-projection";

    /// <summary>
    /// Normalises a packet timestamp to UTC, treating an unspecified kind as already UTC.
    /// </summary>
    /// <remarks>
    /// Only <see cref="DateTimeKind.Local"/> is converted. <c>SpecifyKind(.., Utc)</c> would
    /// relabel a local time without moving it, and <see cref="DateTime.ToUniversalTime"/> would
    /// shift an unspecified one by the machine's offset — a timestamp decoded from a wire format
    /// arrives unspecified, so that assumption invents a time zone the source never stated and
    /// makes the same archive read differently in another region.
    /// </remarks>
    private static DateTime ToUtc(DateTime timestamp) => timestamp.Kind switch
    {
        DateTimeKind.Local => timestamp.ToUniversalTime(),
        _ => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
    };
}
