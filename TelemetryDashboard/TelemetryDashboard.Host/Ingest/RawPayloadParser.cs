using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Parsers;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// Last-resort parsing for frames no routing rule claimed.
/// </summary>
/// <remarks>
/// Fields are named by position — <c>field1</c>, <c>field2</c> — never by guess. The desktop
/// shell's equivalent labels the first four columns Temperature, Humidity, Vibration and RPM, so
/// an unconfigured device streaming four numbers produces a dashboard reading "Temperature 41.9"
/// for a channel nobody identified. A positional name is useless until the operator writes a
/// routing rule, which is exactly the state the system is in.
/// </remarks>
public static class RawPayloadParser
{
    /// <summary>Parses a raw line into packets, or returns an empty list when nothing is readable.</summary>
    public static List<TelemetryPacket> Parse(RawPacket raw)
    {
        var packets = new List<TelemetryPacket>();
        if (string.IsNullOrWhiteSpace(raw.Payload)) return packets;

        string payload = raw.Payload.Trim();

        // A frame that carries a checksum and fails it is corrupt, not unconfigured. Scraping the
        // numbers out of it anyway would publish damaged data as a measurement.
        if (payload.Contains('*') && !XorChecksum.ValidateSpan(payload.AsSpan(), out _)) return packets;

        if (payload.StartsWith('{') && payload.EndsWith('}'))
        {
            // Asked first, because this parser's contract -- one channel per numeric property --
            // is exactly wrong for a frame that names its channel in one field and its reading in
            // another. Falling through to it collapsed every channel of a peer into a single
            // series called 'value' and turned that peer's verdicts into measurements.
            if (PeerFrameParser.Parse(raw, payload) is { } peerSample)
            {
                packets.Add(peerSample);
                return packets;
            }

            // A peer frame that is not a reading. Returned empty rather than fallen through: the
            // stream's opening {"event":"connected","port":N} otherwise became a channel called
            // 'port' holding a TCP port number, which is what this parser is supposed to do with an
            // object it does not recognise and is not what anybody wants done with this one.
            if (PeerFrameParser.IsHousekeeping(payload)) return packets;

            if (TryParseJson(raw, payload, packets)) return packets;
        }

        ParseDelimited(raw, payload, packets);
        return packets;
    }

    /// <summary>Emits one packet per numeric property of a JSON object.</summary>
    private static bool TryParseJson(RawPacket raw, string payload, List<TelemetryPacket> packets)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;

            string node = ResolveNode(document.RootElement, raw.PortName);

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Number &&
                    property.Value.TryGetDouble(out double value))
                {
                    packets.Add(Packet(node, property.Name, value, raw));
                }
            }

            return packets.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Emits one packet per numeric token of a comma, tab or space separated line.</summary>
    private static void ParseDelimited(RawPacket raw, string payload, List<TelemetryPacket> packets)
    {
        string[] tokens = payload.Split(new[] { ',', '\t', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);

        int column = 0;
        foreach (string token in tokens)
        {
            if (double.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            {
                packets.Add(Packet(raw.PortName, $"field{++column}", value, raw));
            }
        }
    }

    private static string ResolveNode(JsonElement root, string fallback)
    {
        foreach (string field in new[] { "nodeId", "device" })
        {
            if (root.TryGetProperty(field, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String)
            {
                string? text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }

        return fallback;
    }

    private static TelemetryPacket Packet(string node, string variable, double value, RawPacket raw) => new()
    {
        NodeId = node,
        Variable = variable,
        Value = value,
        Timestamp = raw.Timestamp,
        RawData = raw.Payload
    };
}
