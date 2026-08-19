using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Core.Protocols;

/// <summary>
/// CAN Bus (automotive / drone) to standard telemetry bridge.
/// </summary>
/// <remarks>
/// Accepts a classic CAN frame laid out as
/// <c>[ id_hi | id_lo | dlc | data0..dataN ]</c> where the identifier is big-endian and the
/// data length code bounds the payload. Every field is bounds-checked against the actual
/// buffer length before it is read.
/// </remarks>
public sealed class CanBusBridgeAdapter : IProtocolBridge
{
    private const int HeaderLength = 3;
    private const int StandardIdMask = 0x7FF;   // 11-bit identifier
    private const int ExtendedIdMask = 0x1FFFFFFF; // 29-bit identifier

    public string ProtocolName => "CANbus";

    /// <summary>True when identifiers should be decoded as 29-bit extended frames.</summary>
    public bool UseExtendedIdentifiers { get; init; }

    public byte[] ConvertToStandardPacket(byte[] rawPayload)
    {
        if (rawPayload == null || rawPayload.Length < HeaderLength)
        {
            return Encode(new { protocol = ProtocolName, error = "frame shorter than CAN header", timestamp = Timestamp() });
        }

        int identifier = (rawPayload[0] << 8) | rawPayload[1];
        identifier &= UseExtendedIdentifiers ? ExtendedIdMask : StandardIdMask;

        // Trust the declared DLC only as far as the buffer actually reaches.
        int declaredLength = rawPayload[2];
        int available = rawPayload.Length - HeaderLength;
        int dataLength = Math.Clamp(declaredLength, 0, Math.Min(8, available));

        var data = new byte[dataLength];
        Buffer.BlockCopy(rawPayload, HeaderLength, data, 0, dataLength);

        var packet = new
        {
            protocol = ProtocolName,
            canId = $"0x{identifier:X3}",
            dlc = declaredLength,
            truncated = declaredLength > dataLength,
            data = Convert.ToHexString(data),
            signals = DecodeBigEndianSignals(data),
            timestamp = Timestamp()
        };

        return Encode(packet);
    }

    public byte[] ConvertFromStandardPacket(object standardTelemetry)
    {
        // Downstream direction: emit the JSON command envelope an edge CAN gateway consumes.
        return Encode(standardTelemetry ?? new { protocol = "CANbus" });
    }

    /// <summary>Interprets the payload as consecutive big-endian 16-bit signals, the common DBC layout.</summary>
    private static IReadOnlyList<int> DecodeBigEndianSignals(byte[] data)
    {
        var signals = new List<int>(data.Length / 2);
        for (int i = 0; i + 1 < data.Length; i += 2)
        {
            signals.Add((data[i] << 8) | data[i + 1]);
        }
        return signals;
    }

    /// <summary>Builds a well-formed CAN frame; used by tests and by the loopback simulator.</summary>
    public static byte[] BuildFrame(int identifier, byte[] data)
    {
        data ??= Array.Empty<byte>();
        int dataLength = Math.Min(8, data.Length);

        var frame = new byte[HeaderLength + dataLength];
        frame[0] = (byte)((identifier >> 8) & 0xFF);
        frame[1] = (byte)(identifier & 0xFF);
        frame[2] = (byte)dataLength;
        Buffer.BlockCopy(data, 0, frame, HeaderLength, dataLength);
        return frame;
    }

    internal static string Timestamp() => DateTime.UtcNow.ToString("o");

    internal static byte[] Encode(object value) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
}
