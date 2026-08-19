using System;
using System.Buffers.Binary;
using System.Text;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Core.Protocols;

/// <summary>
/// ROS2 / DDS (robotics) to standard telemetry bridge.
/// </summary>
/// <remarks>
/// Decodes a CDR-encapsulated message: a 4-byte representation header, a length-prefixed
/// null-terminated topic string, then an 8-byte aligned float64 sample. Both little- and
/// big-endian encapsulations are honoured via the header's representation identifier.
/// </remarks>
public sealed class Ros2BridgeAdapter : IProtocolBridge
{
    private const int EncapsulationHeaderLength = 4;

    public string ProtocolName => "ROS2";

    public byte[] ConvertToStandardPacket(byte[] rawPayload)
    {
        if (rawPayload == null || rawPayload.Length < EncapsulationHeaderLength + 4)
        {
            return CanBusBridgeAdapter.Encode(new
            {
                protocol = ProtocolName,
                error = "payload shorter than CDR encapsulation header",
                timestamp = CanBusBridgeAdapter.Timestamp()
            });
        }

        // Representation identifier: 0x0001 = CDR little-endian, 0x0000 = CDR big-endian.
        bool littleEndian = rawPayload[1] != 0x00;
        ReadOnlySpan<byte> body = rawPayload.AsSpan(EncapsulationHeaderLength);

        int cursor = 0;
        uint topicLength = ReadUInt32(body, ref cursor, littleEndian);
        if (topicLength == 0 || topicLength > body.Length - cursor)
        {
            return CanBusBridgeAdapter.Encode(new
            {
                protocol = ProtocolName,
                error = "declared topic length exceeds payload",
                timestamp = CanBusBridgeAdapter.Timestamp()
            });
        }

        // The declared length includes the null terminator.
        string topic = Encoding.UTF8.GetString(body.Slice(cursor, (int)topicLength - 1));
        cursor += (int)topicLength;

        cursor = Align(cursor, 8);
        double value = 0;
        bool hasValue = cursor + 8 <= body.Length;
        if (hasValue)
        {
            value = littleEndian
                ? BinaryPrimitives.ReadDoubleLittleEndian(body.Slice(cursor, 8))
                : BinaryPrimitives.ReadDoubleBigEndian(body.Slice(cursor, 8));
        }

        return CanBusBridgeAdapter.Encode(new
        {
            protocol = ProtocolName,
            topic,
            value,
            hasValue,
            endianness = littleEndian ? "little" : "big",
            timestamp = CanBusBridgeAdapter.Timestamp()
        });
    }

    public byte[] ConvertFromStandardPacket(object standardTelemetry) =>
        CanBusBridgeAdapter.Encode(standardTelemetry ?? new { protocol = "ROS2" });

    /// <summary>Builds a CDR little-endian encapsulation carrying one float64 topic sample.</summary>
    public static byte[] BuildCdrFloat64Message(string topic, double value)
    {
        topic ??= string.Empty;
        byte[] topicBytes = Encoding.UTF8.GetBytes(topic);

        int cursor = 0;
        cursor += 4;                       // uint32 length prefix
        cursor += topicBytes.Length + 1;   // topic plus null terminator
        int valueOffset = Align(cursor, 8);

        var buffer = new byte[EncapsulationHeaderLength + valueOffset + 8];
        buffer[0] = 0x00;
        buffer[1] = 0x01; // CDR little-endian
        buffer[2] = 0x00;
        buffer[3] = 0x00;

        Span<byte> body = buffer.AsSpan(EncapsulationHeaderLength);
        BinaryPrimitives.WriteUInt32LittleEndian(body, (uint)(topicBytes.Length + 1));
        topicBytes.CopyTo(body.Slice(4));
        body[4 + topicBytes.Length] = 0x00;
        BinaryPrimitives.WriteDoubleLittleEndian(body.Slice(valueOffset, 8), value);

        return buffer;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> body, ref int cursor, bool littleEndian)
    {
        ReadOnlySpan<byte> slice = body.Slice(cursor, 4);
        cursor += 4;
        return littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(slice)
            : BinaryPrimitives.ReadUInt32BigEndian(slice);
    }

    /// <summary>Rounds an offset up to the next CDR alignment boundary.</summary>
    private static int Align(int offset, int boundary)
    {
        int remainder = offset % boundary;
        return remainder == 0 ? offset : offset + (boundary - remainder);
    }
}
