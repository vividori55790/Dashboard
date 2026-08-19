namespace TelemetryDashboard.Core.Models;

[Flags]
public enum PacketFlags
{
    None = 0,
    IsHistorical = 1 << 0,
    IsDerived = 1 << 1,
    ChecksumFailed = 1 << 2,
    AlarmExceeded = 1 << 3,
    Simulated = 1 << 4
}

public sealed class TelemetryPacket
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string NodeId { get; set; } = string.Empty;
    public string Variable { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string RawData { get; set; } = string.Empty;
    public PacketFlags Flags { get; set; } = PacketFlags.None;

    public TelemetryPacket() { }

    public TelemetryPacket(string nodeId, string variable, double value, string unit, DateTime? timestamp = null, PacketFlags flags = PacketFlags.None)
    {
        NodeId = nodeId;
        Variable = variable;
        Value = value;
        Unit = unit;
        Timestamp = timestamp ?? DateTime.UtcNow;
        Flags = flags;
    }
}
