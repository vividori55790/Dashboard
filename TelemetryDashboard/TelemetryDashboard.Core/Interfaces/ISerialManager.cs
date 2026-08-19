namespace TelemetryDashboard.Core.Interfaces;

using System.Threading.Channels;
using TelemetryDashboard.Core.Events;
using TelemetryDashboard.Core.Models;

public interface ISerialManager : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Channel reader for consuming raw incoming packets across all active serial ports.
    /// </summary>
    ChannelReader<RawPacket> PacketReader { get; }

    /// <summary>
    /// Gets the current connection status of all managed COM ports.
    /// </summary>
    IReadOnlyDictionary<string, PortConnectionStatus> ActivePorts { get; }

    /// <summary>
    /// Connects to a specific COM port concurrently.
    /// </summary>
    Task<bool> ConnectPortAsync(string portName, int baudRate = 115200, CancellationToken cancellationToken = default);

    /// <summary>
    /// Convenience overload for ConnectPortAsync.
    /// </summary>
    Task<bool> ConnectAsync(string portName, int baudRate) => ConnectPortAsync(portName, baudRate, CancellationToken.None);

    /// <summary>
    /// Safely disconnects and cleans up a specific COM port worker.
    /// </summary>
    Task DisconnectPortAsync(string portName);

    /// <summary>
    /// Disconnects all active ports cleanly.
    /// </summary>
    Task DisconnectAllAsync();

    /// <summary>
    /// Transmits a string command to a specific connected serial port.
    /// </summary>
    Task WriteLineAsync(string portName, string data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event fired when hardware device arrival/removal occurs.
    /// </summary>
    event EventHandler<DeviceChangeEventArgs>? DeviceChanged;
}
