using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Events;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Infrastructure.Serial;

/// <summary>
/// A serial manager whose ports are in memory, so the serial path can be exercised without a device.
/// </summary>
/// <remarks>
/// This exists for one thing that could not otherwise be checked. The emergency interlock is the
/// only feature in this product that acts on the machine rather than watching it, and it is refused
/// without <c>--serial</c> — so on a workstation with no MCU attached, the furthest anyone could get
/// was "the relay reports itself armed". Whether a command ever reached a port was unverifiable,
/// and an unverifiable safety path is the one most worth verifying.
/// <para>
/// <see cref="MockSerialPort"/> had been written for exactly this and was constructed by nothing.
/// Frames pushed in come back out through the same buffer a device's bytes would, so the parser,
/// the checksum and the routing rules all run on their real inputs. What is <em>not</em> being
/// checked is the driver, the cable and the device: this proves the host wrote the command to the
/// port it was told to, and nothing about what happened after that.
/// </para>
/// </remarks>
public sealed class LoopbackSerialManager : ISerialManager
{
    private readonly ConcurrentDictionary<string, MockSerialPort> _ports = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PortConnectionStatus> _status = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<RawPacket> _packets = Channel.CreateUnbounded<RawPacket>();
    private readonly ConcurrentQueue<string> _written = new();

    private long _writes;

    /// <inheritdoc />
    public ChannelReader<RawPacket> PacketReader => _packets.Reader;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, PortConnectionStatus> ActivePorts => _status;

    /// <inheritdoc />
    public event EventHandler<DeviceChangeEventArgs>? DeviceChanged;

    /// <summary>Commands this host has written to a port, in order.</summary>
    /// <remarks>
    /// Kept so a run can be asked afterwards what it sent, rather than only what it decided. The
    /// two differ whenever a queue drops or a port refuses, and the decision is the easy half.
    /// </remarks>
    public IReadOnlyCollection<string> Written => _written;

    /// <summary>How many commands were written.</summary>
    public long WriteCount => Interlocked.Read(ref _writes);

    /// <inheritdoc />
    public Task<bool> ConnectPortAsync(string portName, int baudRate = 115200, CancellationToken cancellationToken = default)
    {
        MockSerialPort port = _ports.GetOrAdd(portName, name => new MockSerialPort(name, baudRate));
        port.Connect();
        _status[portName] = PortConnectionStatus.Connected;

        DeviceChanged?.Invoke(this, new DeviceChangeEventArgs(DeviceChangeType.Arrival, portName));
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task DisconnectPortAsync(string portName)
    {
        if (_ports.TryGetValue(portName, out MockSerialPort? port)) port.Disconnect();
        _status[portName] = PortConnectionStatus.Disconnected;

        DeviceChanged?.Invoke(this, new DeviceChangeEventArgs(DeviceChangeType.Removal, portName));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task DisconnectAllAsync()
    {
        foreach (string port in _ports.Keys) await DisconnectPortAsync(port).ConfigureAwait(false);
    }

    /// <summary>
    /// Records a command as written to the port, and says so.
    /// </summary>
    /// <remarks>
    /// Announced on stderr rather than counted quietly, because the whole reason this class exists
    /// is to make the write observable from outside the process. A silent loopback would move the
    /// unverifiable step rather than remove it.
    /// </remarks>
    public Task WriteLineAsync(string portName, string data, CancellationToken cancellationToken = default)
    {
        if (!_status.TryGetValue(portName, out PortConnectionStatus status) || status != PortConnectionStatus.Connected)
        {
            // A closed port is a failure, not a no-op. Swallowing it here would let an interlock
            // report a dispatch to a port that was never open.
            throw new InvalidOperationException($"loopback port '{portName}' is not connected.");
        }

        _written.Enqueue(data);
        Interlocked.Increment(ref _writes);
        Console.Error.WriteLine($"[loopback] {portName} <= {data}");

        return Task.CompletedTask;
    }

    /// <summary>Pushes a device frame into a port, as arriving bytes.</summary>
    /// <remarks>
    /// Goes in as a line and comes back out of the port's own buffer, so the frame the ingest path
    /// receives is the one the mock port produced rather than the one the caller held.
    /// </remarks>
    public bool Deliver(string portName, string frame)
    {
        if (!_ports.TryGetValue(portName, out MockSerialPort? port) || !port.IsOpen) return false;

        port.PushLine(frame);

        foreach (string line in port.ReadAvailableLines())
        {
            _packets.Writer.TryWrite(new RawPacket(portName, line, DateTime.UtcNow));
        }

        return true;
    }

    /// <summary>Ends the packet stream so a reader can finish.</summary>
    public void Complete() => _packets.Writer.TryComplete();

    public void Dispose() => Complete();

    public ValueTask DisposeAsync()
    {
        Complete();
        return ValueTask.CompletedTask;
    }
}
