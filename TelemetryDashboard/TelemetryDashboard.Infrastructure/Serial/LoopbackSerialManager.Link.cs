using System;
using System.IO;
using TelemetryDashboard.Core.Events;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Infrastructure.Serial;

/// <summary>
/// Making the link come and go on purpose.
/// </summary>
/// <remarks>
/// A recovery path that has never been seen work is a recovery path nobody should be relying
/// on, and until there was a port with nothing behind it the only way to see one was to pull a
/// connector on a real machine. Kept beside the manager rather than inside it because it is the
/// half that exists for commissioning rather than for carrying data.
/// </remarks>
public sealed partial class LoopbackSerialManager
{
    /// <summary>
    /// Drops the link, the way a cable coming out would.
    /// </summary>
    /// <remarks>
    /// The recovery path is the one an engineer most needs to trust before leaving a rig
    /// unattended, and until now proving it meant pulling a real connector on a real machine. This
    /// raises the same fault the port worker raises when a driver read fails: the status goes to
    /// Faulted, deliveries stop, and whatever is watching the link is told in the same words.
    /// </remarks>
    public bool FaultPort(string portName, string reason)
    {
        if (!_ports.TryGetValue(portName, out MockSerialPort? port)) return false;

        port.Disconnect();
        _status[portName] = PortConnectionStatus.Faulted;

        PortFaulted?.Invoke(this, new SerialPortFaultEventArgs(
            portName, new IOException(reason)));

        return true;
    }
}
