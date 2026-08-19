namespace TelemetryDashboard.Core.Parsers;

using TelemetryDashboard.Core.Models;

public static class JsonParser
{
    public static bool TryParse(
        RawPacket rawPacket, 
        RoutingRule rule, 
        out List<TelemetryPacket> packets)
    {
        packets = new List<TelemetryPacket>();
        string payload = rawPacket.Payload.Trim();
        
        int jsonStart = payload.IndexOf('{');
        int jsonEnd = payload.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd <= jsonStart) return false;

        ReadOnlySpan<byte> utf8Bytes = System.Text.Encoding.UTF8.GetBytes(payload.Substring(jsonStart, jsonEnd - jsonStart + 1));
        var reader = new System.Text.Json.Utf8JsonReader(utf8Bytes);

        string? currentProperty = null;
        string? matchedNode = rule.TargetNodeId;
        Dictionary<string, double> parsedValues = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> stringFields = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            while (reader.Read())
            {
                if (reader.TokenType == System.Text.Json.JsonTokenType.PropertyName)
                {
                    currentProperty = reader.GetString();
                }
                else if (currentProperty != null)
                {
                    if (reader.TokenType == System.Text.Json.JsonTokenType.Number && reader.TryGetDouble(out double numVal))
                    {
                        parsedValues[currentProperty] = numVal;
                        stringFields[currentProperty] = numVal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        currentProperty = null;
                    }
                    else if (reader.TokenType == System.Text.Json.JsonTokenType.String)
                    {
                        string strVal = reader.GetString() ?? string.Empty;
                        stringFields[currentProperty] = strVal;
                        if (double.TryParse(strVal, System.Globalization.CultureInfo.InvariantCulture, out double strNumVal))
                        {
                            parsedValues[currentProperty] = strNumVal;
                        }
                        currentProperty = null;
                    }
                    else if (reader.TokenType == System.Text.Json.JsonTokenType.True || reader.TokenType == System.Text.Json.JsonTokenType.False)
                    {
                        string boolStr = reader.GetBoolean().ToString().ToLowerInvariant();
                        stringFields[currentProperty] = boolStr;
                        currentProperty = null;
                    }
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }

        // Validate pattern match (e.g. "device:PSFB" or "PSFB")
        if (!string.IsNullOrEmpty(rule.Tag))
        {
            if (rule.Tag.Contains(':'))
            {
                var kv = rule.Tag.Split(':', 2);
                if (!stringFields.TryGetValue(kv[0], out var actualVal) || !string.Equals(actualVal, kv[1], StringComparison.OrdinalIgnoreCase)) return false;
            }
            else
            {
                bool matched = stringFields.ContainsKey(rule.Tag) ||
                               stringFields.Values.Any(v => string.Equals(v, rule.Tag, StringComparison.OrdinalIgnoreCase));
                if (!matched) return false;
            }
        }

        if (rule.JsonMap.Count > 0)
        {
            foreach (var (jsonKey, varName) in rule.JsonMap)
            {
                if (parsedValues.TryGetValue(jsonKey, out double rawVal))
                {
                    double calVal = rawVal;
                    if (rule.Calibrations.TryGetValue(varName, out var cal))
                    {
                        calVal = rawVal * cal.Gain + cal.Offset;
                    }

                    packets.Add(new TelemetryPacket
                    {
                        NodeId = stringFields.TryGetValue("nodeId", out var nid) ? nid : (stringFields.TryGetValue("node", out var n) ? n : matchedNode),
                        Variable = varName,
                        Value = calVal,
                        Unit = stringFields.TryGetValue("unit", out var u) ? u : "",
                        Timestamp = rawPacket.Timestamp,
                        RawData = rawPacket.Payload,
                        Flags = PacketFlags.None
                    });
                }
            }
        }
        else
        {
            string nodeId = stringFields.TryGetValue("nodeId", out var nid) ? nid :
                           (stringFields.TryGetValue("node", out var n) ? n : matchedNode);
            string unit = stringFields.TryGetValue("unit", out var u) ? u : "";

            if (stringFields.TryGetValue("variable", out var vName) && parsedValues.TryGetValue("value", out var vVal))
            {
                double calVal = vVal;
                if (rule.Calibrations.TryGetValue(vName, out var cal))
                {
                    calVal = vVal * cal.Gain + cal.Offset;
                }

                packets.Add(new TelemetryPacket
                {
                    NodeId = nodeId,
                    Variable = vName,
                    Value = calVal,
                    Unit = unit,
                    Timestamp = rawPacket.Timestamp,
                    RawData = rawPacket.Payload,
                    Flags = PacketFlags.None
                });
            }
            else if (stringFields.TryGetValue("var", out var vName2) && parsedValues.TryGetValue("val", out var vVal2))
            {
                double calVal = vVal2;
                if (rule.Calibrations.TryGetValue(vName2, out var cal))
                {
                    calVal = vVal2 * cal.Gain + cal.Offset;
                }

                packets.Add(new TelemetryPacket
                {
                    NodeId = nodeId,
                    Variable = vName2,
                    Value = calVal,
                    Unit = unit,
                    Timestamp = rawPacket.Timestamp,
                    RawData = rawPacket.Payload,
                    Flags = PacketFlags.None
                });
            }
            else
            {
                foreach (var (key, rawVal) in parsedValues)
                {
                    if (string.Equals(key, "timestamp", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, "time", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, "ts", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    double calVal = rawVal;
                    if (rule.Calibrations.TryGetValue(key, out var cal))
                    {
                        calVal = rawVal * cal.Gain + cal.Offset;
                    }

                    packets.Add(new TelemetryPacket
                    {
                        NodeId = nodeId,
                        Variable = key,
                        Value = calVal,
                        Unit = unit,
                        Timestamp = rawPacket.Timestamp,
                        RawData = rawPacket.Payload,
                        Flags = PacketFlags.None
                    });
                }
            }
        }

        return packets.Count > 0;
    }
}
