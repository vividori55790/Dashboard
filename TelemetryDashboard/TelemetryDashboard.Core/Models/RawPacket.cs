namespace TelemetryDashboard.Core.Models;

/// <summary>
/// Immutable record struct representing an unparsed raw line received from a serial port.
/// </summary>
public readonly record struct RawPacket
{
    public string PortName { get; init; }
    public string RawLine { get; init; }
    public DateTime TimestampUtc { get; init; }

    public string Payload
    {
        get => RawLine;
        init => RawLine = value;
    }

    public string RawData => RawLine;

    public DateTime Timestamp
    {
        get => TimestampUtc;
        init => TimestampUtc = value;
    }

    public RawPacket(string portName, string rawLine, DateTime timestampUtc)
    {
        PortName = portName;
        RawLine = rawLine;
        TimestampUtc = timestampUtc;
    }

    public RawPacket(string portName, string rawLine)
        : this(portName, rawLine, DateTime.UtcNow)
    {
    }
}
