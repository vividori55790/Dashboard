using System;

namespace TelemetryDashboard.Core.Events;

/// <summary>Why a port stopped producing lines, and what the driver said about it.</summary>
/// <remarks>
/// Carries the cause rather than just the fact. "The cable was pulled" and "the driver refused a
/// read" both stop the data, and an operator standing at the rack needs to know which one before
/// deciding whether to reseat a connector or look at the machine.
/// </remarks>
public sealed class SerialPortFaultEventArgs : EventArgs
{
    public SerialPortFaultEventArgs(string portName, Exception? cause)
    {
        PortName = portName;
        Cause = cause;
    }

    public string PortName { get; }

    /// <summary>The driver-level failure, or null when the port simply closed under us.</summary>
    public Exception? Cause { get; }

    public string Describe() => Cause is null
        ? $"port {PortName} closed unexpectedly"
        : $"port {PortName} failed: {Cause.GetType().Name}: {Cause.Message}";
}
