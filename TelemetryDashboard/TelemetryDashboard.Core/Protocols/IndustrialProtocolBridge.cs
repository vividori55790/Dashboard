using System;
using System.Buffers.Binary;
using System.Text;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Core.Protocols;

/// <summary>
/// Facade that sniffs an unlabelled industrial frame and dispatches it to the matching adapter
/// in a <see cref="ProtocolBridgeRegistry"/>.
/// </summary>
/// <remarks>
/// This exists for ingest paths where the wire protocol is not known ahead of time. When the
/// protocol <em>is</em> known, resolve the adapter from the registry directly instead — sniffing
/// is a heuristic, not an identification.
/// </remarks>
public class IndustrialProtocolBridge : IProtocolBridge
{
    /// <summary>Marker byte pair identifying the legacy 10-byte fixed CAN telemetry frame.</summary>
    private const byte LegacyCanMarkerHigh = 0x08;
    private const byte LegacyCanMarkerLow = 0x00;
    private const int LegacyCanFrameLength = 10;

    private readonly ProtocolBridgeRegistry _registry;

    public string ProtocolName { get; }

    public IndustrialProtocolBridge(string protocolName = "CANbus_Modbus_ROS2")
        : this(protocolName, ProtocolBridgeRegistry.CreateDefault())
    {
    }

    public IndustrialProtocolBridge(string protocolName, ProtocolBridgeRegistry registry)
    {
        ProtocolName = protocolName;
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>The adapter registry backing this facade, so callers can add protocols at runtime.</summary>
    public ProtocolBridgeRegistry Registry => _registry;

    public byte[] ConvertToStandardPacket(byte[] rawPayload)
    {
        if (rawPayload == null || rawPayload.Length == 0) return Array.Empty<byte>();

        if (TryDecodeLegacyCanFrame(rawPayload, out byte[] legacyPacket))
        {
            return legacyPacket;
        }

        if (LooksLikeModbus(rawPayload) && _registry.TryResolve("ModbusRTU", out IProtocolBridge modbus))
        {
            return modbus.ConvertToStandardPacket(rawPayload);
        }

        if (LooksLikeCdr(rawPayload) && _registry.TryResolve("ROS2", out IProtocolBridge ros2))
        {
            return ros2.ConvertToStandardPacket(rawPayload);
        }

        // Nothing matched: surface the raw frame rather than inventing decoded values for it.
        return CanBusBridgeAdapter.Encode(new
        {
            protocol = ProtocolName,
            recognized = false,
            rawText = Encoding.UTF8.GetString(rawPayload),
            rawHex = Convert.ToHexString(rawPayload),
            len = rawPayload.Length,
            timestamp = CanBusBridgeAdapter.Timestamp()
        });
    }

    public byte[] ConvertFromStandardPacket(object standardTelemetry) =>
        CanBusBridgeAdapter.Encode(standardTelemetry ?? new { protocol = ProtocolName });

    /// <summary>
    /// Decodes the fixed 10-byte frame <c>[08 00 | id(u32) | value(f32)]</c> emitted by the
    /// bundled STM32 sample firmware. The previous implementation accepted any frame of eight or
    /// more bytes and then read a float at offset six, running four bytes past a minimal frame.
    /// </summary>
    private bool TryDecodeLegacyCanFrame(byte[] payload, out byte[] packet)
    {
        packet = Array.Empty<byte>();

        if (payload.Length < LegacyCanFrameLength) return false;
        if (payload[0] != LegacyCanMarkerHigh || payload[1] != LegacyCanMarkerLow) return false;

        uint canId = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(2, 4));
        float value = BitConverter.ToSingle(payload, 6);

        packet = CanBusBridgeAdapter.Encode(new
        {
            protocol = "CANbus",
            canId = $"0x{canId:X3}",
            temp = value,
            timestamp = CanBusBridgeAdapter.Timestamp()
        });
        return true;
    }

    /// <summary>Heuristic: a plausible slave address followed by a supported read function code.</summary>
    private static bool LooksLikeModbus(byte[] payload) =>
        payload.Length >= 5 &&
        payload[0] is >= 0x01 and <= 0xF7 &&
        payload[1] is 0x01 or 0x02 or 0x03 or 0x04;

    /// <summary>Heuristic: a CDR encapsulation header with a recognised representation identifier.</summary>
    private static bool LooksLikeCdr(byte[] payload) =>
        payload.Length >= 8 &&
        payload[0] == 0x00 &&
        payload[1] is 0x00 or 0x01 or 0x02 or 0x03;
}
