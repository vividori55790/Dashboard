namespace TelemetryDashboard.Infrastructure.Serial;

using System.IO.Ports;
using System.Threading.Channels;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Events;

/// <summary>
/// Owns one open serial port and pushes each complete line onto the shared packet channel.
/// </summary>
/// <remarks>
/// It reports its own death, and that is the point of the type existing separately. The read loop
/// used to end in a bare <c>catch { }</c>: pulling the cable threw an <see cref="IOException"/>,
/// the loop exited, and nothing else changed — the port stayed marked <c>Connected</c>, the worker
/// stayed in the manager's table, and because the connect path returns early for a port it already
/// holds, reconnecting became impossible for the lifetime of the process. From the operator's seat
/// that is a dashboard that stops updating and never says why, and an auto-reconnect feature that
/// can never fire. A worker that announces its own failure is what makes recovery possible at all.
/// </remarks>
internal sealed class SerialPortWorker
{
    private readonly string _portName;
    private readonly int _baudRate;
    private readonly ChannelWriter<RawPacket> _writer;
    private readonly SerialLineAssembler _lines = new();
    private SerialPort? _serialPort;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private int _faultAnnounced;

    public SerialPortWorker(string portName, int baudRate, ChannelWriter<RawPacket> writer)
    {
        _portName = portName;
        _baudRate = baudRate;
        _writer = writer;
    }

    /// <summary>Raised once when the port dies for any reason other than a requested stop.</summary>
    public event EventHandler<SerialPortFaultEventArgs>? Faulted;

    /// <summary>Partial lines this port abandoned for being implausibly long.</summary>
    public long OverlongDiscards => _lines.OverlongDiscards;

    public Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _serialPort = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One)
            {
                ReadBufferSize = 65536,
                WriteBufferSize = 4096,
                ReadTimeout = 500,
                WriteTimeout = 500
            };

            _serialPort.Open();
            _cts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoopAsync(_serialPort, _cts.Token));
            return Task.FromResult(true);
        }
        catch
        {
            _serialPort?.Dispose();
            return Task.FromResult(false);
        }
    }

    public async Task WriteLineAsync(string data, CancellationToken cancellationToken)
    {
        if (_serialPort is null || !_serialPort.IsOpen) return;

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(data);
        await _serialPort.BaseStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
        await _serialPort.BaseStream.FlushAsync(cancellationToken);
    }

    private async Task ReadLoopAsync(SerialPort port, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[4096];
        Exception? cause = null;

        try
        {
            Stream stream = port.BaseStream;

            while (!cancellationToken.IsCancellationRequested && port.IsOpen)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0) continue;

                _lines.Append(
                    buffer.AsSpan(0, bytesRead),
                    line => _writer.TryWrite(new RawPacket(_portName, line, DateTime.UtcNow)));
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            cause = ex;
        }

        // Reached only when the loop ended without being asked to. A requested stop cancels the
        // token and returns above, so this cannot fire on an orderly shutdown.
        if (!cancellationToken.IsCancellationRequested) AnnounceFault(cause);
    }

    private void AnnounceFault(Exception? cause)
    {
        if (Interlocked.Exchange(ref _faultAnnounced, 1) != 0) return;

        try { if (_serialPort?.IsOpen == true) _serialPort.Close(); }
        catch { /* The port is already gone; closing it is best effort. */ }

        Faulted?.Invoke(this, new SerialPortFaultEventArgs(_portName, cause));
    }

    public async Task StopAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            if (_readTask != null)
            {
                await Task.WhenAny(_readTask, Task.Delay(300));
            }
            _cts.Dispose();
        }

        if (_serialPort != null)
        {
            try { if (_serialPort.IsOpen) _serialPort.Close(); }
            catch { /* Already closed or the device is gone. */ }
            _serialPort.Dispose();
        }
    }
}
