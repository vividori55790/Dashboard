using System.Collections.Generic;
using System.Text;

namespace TelemetryDashboard.Infrastructure.WebServer;

/// <summary>
/// Encodes and decodes the MQTT 3.1.1 control packets the hub needs: CONNECT, CONNACK, PUBLISH.
/// </summary>
/// <remarks>
/// Split out from <see cref="MqttPublisher"/> because the wire format is fixed by the
/// specification while connection lifecycle and queueing are ours to change. Keeping the codec
/// pure and static also lets the byte layout be exercised without a socket or a broker.
/// </remarks>
internal static class MqttWireProtocol
{
    private const byte PacketConnect = 0x10;
    private const byte PacketConnAck = 0x20;
    private const byte PacketPublish = 0x30;
    private const byte ConnAckAccepted = 0x00;

    /// <summary>Builds an MQTT 3.1.1 CONNECT packet.</summary>
    internal static byte[] BuildConnectPacket(string clientId, string? username, string? password)
    {
        var payload = new List<byte>();
        AppendString(payload, clientId);

        byte flags = 0x02; // clean session
        if (!string.IsNullOrEmpty(username))
        {
            flags |= 0x80;
            AppendString(payload, username);
        }
        if (!string.IsNullOrEmpty(password))
        {
            flags |= 0x40;
            AppendString(payload, password);
        }

        var variableHeader = new List<byte>();
        AppendString(variableHeader, "MQTT");
        variableHeader.Add(0x04);  // protocol level 4 == 3.1.1
        variableHeader.Add(flags);
        variableHeader.Add(0x00);  // keep-alive high byte
        variableHeader.Add(0x3C);  // keep-alive low byte (60s)

        return Assemble(PacketConnect, variableHeader, payload);
    }

    /// <summary>Builds an MQTT PUBLISH packet at QoS 0 (no packet identifier).</summary>
    internal static byte[] BuildPublishPacket(string topic, string payload)
    {
        var variableHeader = new List<byte>();
        AppendString(variableHeader, topic);

        var body = new List<byte>(Encoding.UTF8.GetBytes(payload));
        return Assemble(PacketPublish, variableHeader, body);
    }

    /// <summary>Reads a broker's CONNACK reply and reports whether the session was accepted.</summary>
    internal static bool IsConnAckAccepted(byte[] response, int bytesRead) =>
        // CONNACK is four bytes; the last carries the return code.
        bytesRead >= 4 && response[0] == PacketConnAck && response[3] == ConnAckAccepted;

    private static byte[] Assemble(byte packetType, List<byte> variableHeader, List<byte> payload)
    {
        var packet = new List<byte> { packetType };
        packet.AddRange(EncodeRemainingLength(variableHeader.Count + payload.Count));
        packet.AddRange(variableHeader);
        packet.AddRange(payload);
        return packet.ToArray();
    }

    private static void AppendString(List<byte> target, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        target.Add((byte)(bytes.Length >> 8));
        target.Add((byte)(bytes.Length & 0xFF));
        target.AddRange(bytes);
    }

    /// <summary>MQTT encodes lengths as 1-4 continuation-flagged bytes.</summary>
    private static byte[] EncodeRemainingLength(int length)
    {
        var bytes = new List<byte>(4);
        do
        {
            byte digit = (byte)(length % 128);
            length /= 128;
            if (length > 0) digit |= 0x80;
            bytes.Add(digit);
        }
        while (length > 0);

        return bytes.ToArray();
    }
}
