namespace TelemetryDashboard.Infrastructure.Serial;

using System;

/// <summary>What the reconnect engine did to a port, and from when it asked the device to resend.</summary>
/// <remarks>
/// The last timestamp is carried because it is the part worth reading. A resync from the moment the
/// link dropped is a few seconds of history; a resync from the moment the port was first opened is
/// the whole session, and the two are told apart only by this number.
/// </remarks>
public sealed class PortLinkEventArgs : EventArgs
{
    public PortLinkEventArgs(string portName, int baudRate, DateTime resyncFromUtc, string reason = "")
    {
        PortName = portName;
        BaudRate = baudRate;
        ResyncFromUtc = resyncFromUtc;
        Reason = reason;
    }

    public string PortName { get; }

    public int BaudRate { get; }

    /// <summary>The instant the device was asked to resend from.</summary>
    public DateTime ResyncFromUtc { get; }

    /// <summary>Why an attempt failed, or empty. Only meaningful on a failure.</summary>
    public string Reason { get; }
}
