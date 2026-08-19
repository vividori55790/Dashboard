namespace TelemetryDashboard.Infrastructure.Serial;

using TelemetryDashboard.Core.Models;

/// <summary>
/// Immutable result structure for auto-baud rate and format scanning.
/// </summary>
public readonly record struct ScanResult(
    bool IsSuccess,
    int DetectedBaudRate,
    PacketFormat DetectedFormat,
    string PortName
);
