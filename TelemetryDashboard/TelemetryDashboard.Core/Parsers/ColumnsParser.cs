namespace TelemetryDashboard.Core.Parsers;

using TelemetryDashboard.Core.Models;

public static class ColumnsParser
{
    public static bool TryParse(
        RawPacket rawPacket, 
        RoutingRule rule, 
        out List<TelemetryPacket> packets)
    {
        packets = new List<TelemetryPacket>();
        ReadOnlySpan<char> span = rawPacket.Payload.AsSpan().Trim();
        if (span.StartsWith("$")) return false;

        // Clean checksum suffix if present
        int starIdx = span.LastIndexOf('*');
        if (starIdx >= 0) span = span.Slice(0, starIdx);

        int colIndex = 0;
        int sliceStart = 0;
        for (int i = 0; i <= span.Length; i++)
        {
            if (i == span.Length || span[i] == ',')
            {
                ReadOnlySpan<char> token = span.Slice(sliceStart, i - sliceStart).Trim();
                if (rule.IndexMap.TryGetValue(colIndex, out string? varName))
                {
                    if (double.TryParse(token, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double rawVal))
                    {
                        double calVal = rawVal;
                        if (rule.Calibrations.TryGetValue(varName, out var cal))
                        {
                            calVal = rawVal * cal.Gain + cal.Offset;
                        }

                        packets.Add(new TelemetryPacket
                        {
                            NodeId = rule.TargetNodeId,
                            Variable = varName,
                            Value = calVal,
                            Timestamp = rawPacket.Timestamp,
                            RawData = rawPacket.Payload,
                            Flags = PacketFlags.None
                        });
                    }
                }
                colIndex++;
                sliceStart = i + 1;
            }
        }

        return packets.Count > 0;
    }
}
