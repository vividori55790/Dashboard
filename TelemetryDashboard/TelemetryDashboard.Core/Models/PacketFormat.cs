namespace TelemetryDashboard.Core.Models;

/// <summary>
/// Packet format classification for raw telemetry payload detection.
/// </summary>
public enum PacketFormat
{
    Unknown = 0,
    Prefix = 1,
    Json = 2,
    Columns = 3,
    Hex = 4
}
