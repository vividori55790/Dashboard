using System;
using System.Collections.Generic;
using System.Text;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Core.Protocols;

/// <summary>
/// Modbus RTU/TCP (industrial power, UPS) to standard telemetry bridge.
/// </summary>
/// <remarks>
/// Decodes a read-holding/input-registers response
/// <c>[ slaveId | function | byteCount | data.. | crcLo | crcHi ]</c> and validates the
/// CRC-16/MODBUS trailer. A frame that fails its checksum is reported with
/// <c>crcValid: false</c> rather than being silently accepted as real measurements.
/// </remarks>
public sealed class ModbusBridgeAdapter : IProtocolBridge
{
    private const int MinimumFrameLength = 5; // slave + function + count + 2 CRC bytes

    public string ProtocolName => "ModbusRTU";

    /// <summary>Scale applied to raw register counts when producing engineering units.</summary>
    public double RegisterScale { get; init; } = 1.0;

    public byte[] ConvertToStandardPacket(byte[] rawPayload)
    {
        if (rawPayload == null || rawPayload.Length < MinimumFrameLength)
        {
            return CanBusBridgeAdapter.Encode(new
            {
                protocol = ProtocolName,
                error = "frame shorter than Modbus RTU minimum",
                timestamp = CanBusBridgeAdapter.Timestamp()
            });
        }

        bool crcValid = VerifyCrc(rawPayload);

        byte slaveId = rawPayload[0];
        byte functionCode = rawPayload[1];
        int declaredByteCount = rawPayload[2];

        // Bound the register block by what the frame actually carries, excluding the CRC trailer.
        int available = rawPayload.Length - 3 - 2;
        int byteCount = Math.Clamp(declaredByteCount, 0, Math.Max(0, available));

        var registers = new List<int>(byteCount / 2);
        for (int i = 0; i + 1 < byteCount; i += 2)
        {
            registers.Add((rawPayload[3 + i] << 8) | rawPayload[3 + i + 1]);
        }

        var scaled = new double[registers.Count];
        for (int i = 0; i < registers.Count; i++)
        {
            scaled[i] = registers[i] * RegisterScale;
        }

        return CanBusBridgeAdapter.Encode(new
        {
            protocol = ProtocolName,
            slaveId,
            functionCode,
            crcValid,
            byteCount = declaredByteCount,
            truncated = declaredByteCount > byteCount,
            registers,
            values = scaled,
            timestamp = CanBusBridgeAdapter.Timestamp()
        });
    }

    public byte[] ConvertFromStandardPacket(object standardTelemetry) =>
        CanBusBridgeAdapter.Encode(standardTelemetry ?? new { protocol = "ModbusRTU" });

    /// <summary>Builds a CRC-terminated RTU response frame. Used by tests and the loopback simulator.</summary>
    public static byte[] BuildRtuResponse(byte slaveId, byte functionCode, byte[] registerData)
    {
        registerData ??= Array.Empty<byte>();

        var frame = new byte[3 + registerData.Length + 2];
        frame[0] = slaveId;
        frame[1] = functionCode;
        frame[2] = (byte)registerData.Length;
        Buffer.BlockCopy(registerData, 0, frame, 3, registerData.Length);

        ushort crc = ComputeCrc(frame, 0, frame.Length - 2);
        frame[^2] = (byte)(crc & 0xFF);        // CRC low byte first, per the Modbus spec
        frame[^1] = (byte)((crc >> 8) & 0xFF);
        return frame;
    }

    private static bool VerifyCrc(byte[] frame)
    {
        ushort expected = ComputeCrc(frame, 0, frame.Length - 2);
        ushort actual = (ushort)(frame[^2] | (frame[^1] << 8));
        return expected == actual;
    }

    /// <summary>CRC-16/MODBUS: reflected polynomial 0xA001, seed 0xFFFF.</summary>
    private static ushort ComputeCrc(byte[] buffer, int offset, int length)
    {
        ushort crc = 0xFFFF;
        for (int i = offset; i < offset + length; i++)
        {
            crc ^= buffer[i];
            for (int bit = 0; bit < 8; bit++)
            {
                bool lsb = (crc & 1) != 0;
                crc >>= 1;
                if (lsb) crc ^= 0xA001;
            }
        }
        return crc;
    }
}
