namespace TelemetryDashboard.Core.Models;

using System.Collections.Concurrent;

public enum NodeStatus
{
    Offline,
    Connecting,
    Online,
    Error,
    Resyncing
}

public sealed class SensorNode
{
    public string NodeId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Port { get; set; } = string.Empty;
    public string Subsystem { get; set; } = string.Empty;
    public NodeStatus Status { get; set; } = NodeStatus.Offline;
    public DateTime LastSeen { get; set; } = DateTime.MinValue;
    public string FirmwareVersion { get; set; } = "v1.0.0";

    public ConcurrentDictionary<string, double> LatestValues { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, (double Min, double Max)> Thresholds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public SensorNode() { }

    public SensorNode(string nodeId, string name, string port, string subsystem)
    {
        NodeId = nodeId;
        Name = name;
        Port = port;
        Subsystem = subsystem;
    }

    public bool UpdateVariable(string variable, double value)
    {
        LatestValues[variable] = value;
        LastSeen = DateTime.UtcNow;
        
        if (Thresholds.TryGetValue(variable, out var bounds))
        {
            return value < bounds.Min || value > bounds.Max;
        }
        return false;
    }
}
