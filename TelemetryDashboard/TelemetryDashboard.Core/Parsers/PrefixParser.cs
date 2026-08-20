namespace TelemetryDashboard.Core.Parsers;

using TelemetryDashboard.Core.Models;

public static class PrefixParser
{
    public static bool TryParse(
        RawPacket rawPacket, 
        RoutingRule rule, 
        out List<TelemetryPacket> packets)
    {
        packets = new List<TelemetryPacket>();
        if (string.IsNullOrWhiteSpace(rawPacket.Payload)) return false;

        ReadOnlySpan<char> span = rawPacket.Payload.AsSpan().Trim();

        // 1. Check for historical resync format: $HIST,node,var,val,ts
        if (span.StartsWith("$HIST,"))
        {
            return TryParseHistorical(rawPacket, span, out packets);
        }

        // 2. Standard XOR checksum verification
        ReadOnlySpan<char> content;
        if (span.Contains('*'))
        {
            if (!XorChecksum.ValidateSpan(span, out content))
            {
                return false;
            }
        }
        else
        {
            content = span.StartsWith("$") ? span.Slice(1) : span;
        }

        // 3. Match prefix tag
        string tag = rule.Tag.StartsWith("$") ? rule.Tag[1..] : rule.Tag;
        if (!content.StartsWith(tag, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (content.Length > tag.Length && content[tag.Length] != ',')
        {
            return false;
        }

        // Strip tag and leading comma
        ReadOnlySpan<char> dataSpan = content.Slice(tag.Length);
        if (dataSpan.StartsWith(",")) dataSpan = dataSpan.Slice(1);

        // 4. Tokenize CSV fields
        var tokens = new List<string>();
        int sStart = 0;
        for (int i = 0; i <= dataSpan.Length; i++)
        {
            if (i == dataSpan.Length || dataSpan[i] == ',')
            {
                tokens.Add(dataSpan.Slice(sStart, i - sStart).Trim().ToString());
                sStart = i + 1;
            }
        }

        if (rule.IndexMap.Count > 0)
        {
            int offset = (tokens.Count > 0 && double.TryParse(tokens[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _)) ? 0 : 1;
            string targetNodeId = (offset == 1 && tokens.Count > 0 && !string.IsNullOrEmpty(tokens[0])) ? tokens[0] : rule.TargetNodeId;

            for (int colIndex = 0; colIndex < rule.IndexMap.Count; colIndex++)
            {
                int tokenIdx = colIndex + offset;
                if (tokenIdx < tokens.Count && rule.IndexMap.TryGetValue(colIndex, out string? varName))
                {
                    if (double.TryParse(tokens[tokenIdx], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double rawVal))
                    {
                        double calVal = rawVal;
                        if (rule.Calibrations.TryGetValue(varName, out var cal))
                        {
                            calVal = rawVal * cal.Gain + cal.Offset;
                        }

                        packets.Add(new TelemetryPacket
                        {
                            NodeId = targetNodeId,
                            Variable = varName,
                            Value = calVal,
                            Unit = tokens.Count > tokenIdx + 1 && !double.TryParse(tokens[tokenIdx + 1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _) ? tokens[tokenIdx + 1] : "",
                            Timestamp = rawPacket.Timestamp,
                            RawData = rawPacket.Payload,
                            Flags = PacketFlags.None
                        });
                    }
                }
            }

            if (packets.Count > 0) return true;

            if (tokens.Count >= 3 && double.TryParse(tokens[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val3))
            {
                string varName = rule.IndexMap.TryGetValue(0, out string? mapVar) && mapVar != tokens[0] ? mapVar : tokens[1];
                double calVal = val3;
                if (rule.Calibrations.TryGetValue(varName, out var cal))
                {
                    calVal = val3 * cal.Gain + cal.Offset;
                }

                packets.Add(new TelemetryPacket
                {
                    NodeId = !string.IsNullOrEmpty(tokens[0]) ? tokens[0] : rule.TargetNodeId,
                    Variable = varName,
                    Value = calVal,
                    Unit = tokens.Count >= 4 ? tokens[3] : "",
                    Timestamp = rawPacket.Timestamp,
                    RawData = rawPacket.Payload,
                    Flags = PacketFlags.None
                });
                return true;
            }
        }
        else
        {
            if (tokens.Count >= 3 && double.TryParse(tokens[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val3))
            {
                packets.Add(new TelemetryPacket
                {
                    NodeId = !string.IsNullOrEmpty(tokens[0]) ? tokens[0] : rule.TargetNodeId,
                    Variable = tokens[1],
                    Value = val3,
                    Unit = tokens.Count >= 4 ? tokens[3] : "",
                    Timestamp = rawPacket.Timestamp,
                    RawData = rawPacket.Payload,
                    Flags = PacketFlags.None
                });
                return true;
            }

            if (tokens.Count >= 2 && double.TryParse(tokens[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val2))
            {
                packets.Add(new TelemetryPacket
                {
                    NodeId = rule.TargetNodeId,
                    Variable = tokens[0],
                    Value = val2,
                    Unit = tokens.Count >= 3 ? tokens[2] : "",
                    Timestamp = rawPacket.Timestamp,
                    RawData = rawPacket.Payload,
                    Flags = PacketFlags.None
                });
                return true;
            }

            if (tokens.Count >= 1 && double.TryParse(tokens[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val1))
            {
                packets.Add(new TelemetryPacket
                {
                    NodeId = rule.TargetNodeId,
                    Variable = "value",
                    Value = val1,
                    Unit = tokens.Count >= 2 ? tokens[1] : "",
                    Timestamp = rawPacket.Timestamp,
                    RawData = rawPacket.Payload,
                    Flags = PacketFlags.None
                });
                return true;
            }
        }

        return packets.Count > 0;
    }

    private static bool TryParseHistorical(
        RawPacket rawPacket, 
        ReadOnlySpan<char> span, 
        out List<TelemetryPacket> packets)
    {
        packets = new List<TelemetryPacket>();
        // Format: $HIST,node,var,val,ts
        string[] parts = span.ToString().Split(',');
        if (parts.Length >= 5)
        {
            string node = parts[1];
            string varName = parts[2];
            if (double.TryParse(parts[3], System.Globalization.CultureInfo.InvariantCulture, out double val) &&
                double.TryParse(parts[4], System.Globalization.CultureInfo.InvariantCulture, out double tsOffset))
            {
                DateTime pktTime;
                try
                {
                    pktTime = DateTime.UnixEpoch.AddSeconds(tsOffset);
                }
                catch
                {
                    pktTime = DateTime.MaxValue;
                }
                packets.Add(new TelemetryPacket
                {
                    NodeId = node,
                    Variable = varName,
                    Value = val,
                    Timestamp = pktTime,
                    RawData = rawPacket.Payload,
                    Flags = PacketFlags.IsHistorical
                });
                return true;
            }
        }
        return false;
    }
}
