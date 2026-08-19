using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TelemetryDashboard.Tests.TestUtilities;

/// <summary>
/// Synthetic COM stream generator simulating hardware serial device behavior.
/// Generates synthetic telemetry streams, handles mock connects/disconnects,
/// and feeds raw byte/line data to subscribers.
/// </summary>
public class MockSerialDevice
{
    private readonly ConcurrentQueue<string> _lineBuffer = new();
    private readonly ConcurrentQueue<byte> _byteBuffer = new();
    private readonly object _stateLock = new();

    public string PortName { get; set; }
    public int BaudRate { get; set; }
    public bool IsOpen { get; private set; }
    public int PendingLinesCount => _lineBuffer.Count;
    public int PendingBytesCount => _byteBuffer.Count;

    public event EventHandler<string>? LineReceived;
    public event EventHandler<byte[]>? DataReceived;
    public event EventHandler<bool>? ConnectionStateChanged;

    public MockSerialDevice(string portName = "COM3", int baudRate = 115200)
    {
        PortName = portName;
        BaudRate = baudRate;
        IsOpen = false;
    }

    /// <summary>
    /// Opens the mock COM connection.
    /// </summary>
    public bool Connect()
    {
        lock (_stateLock)
        {
            if (IsOpen) return false;
            IsOpen = true;
        }
        ConnectionStateChanged?.Invoke(this, true);
        return true;
    }

    /// <summary>
    /// Closes the mock COM connection.
    /// </summary>
    public bool Disconnect()
    {
        lock (_stateLock)
        {
            if (!IsOpen) return false;
            IsOpen = false;
        }
        ConnectionStateChanged?.Invoke(this, false);
        return true;
    }

    /// <summary>
    /// Simulates hardware plug-in event.
    /// </summary>
    public void SimulateDevicePlugIn() => Connect();

    /// <summary>
    /// Simulates hardware unplug event.
    /// </summary>
    public void SimulateDeviceUnplug() => Disconnect();

    /// <summary>
    /// Pushes a single line into the synthetic stream and notifies subscribers.
    /// </summary>
    public void PushLine(string line)
    {
        if (line == null) return;
        _lineBuffer.Enqueue(line);

        var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        foreach (var b in bytes)
        {
            _byteBuffer.Enqueue(b);
        }

        if (IsOpen)
        {
            LineReceived?.Invoke(this, line);
            DataReceived?.Invoke(this, bytes);
        }
    }

    /// <summary>
    /// Pushes raw byte array into the synthetic stream.
    /// </summary>
    public void PushBytes(byte[] data)
    {
        if (data == null || data.Length == 0) return;
        foreach (var b in data)
        {
            _byteBuffer.Enqueue(b);
        }

        if (IsOpen)
        {
            DataReceived?.Invoke(this, data);
            var str = Encoding.UTF8.GetString(data);
            if (str.Contains('\n') || str.Contains('\r'))
            {
                var lines = str.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var l in lines)
                {
                    _lineBuffer.Enqueue(l);
                    LineReceived?.Invoke(this, l);
                }
            }
        }
    }

    /// <summary>
    /// Pushes a formatted PREFIX telemetry frame with valid XOR checksum.
    /// Format: $[TAG],[NODE],[VAR],[VAL],[UNIT]*[XOR]
    /// </summary>
    public string PushPrefixFrame(string tag, string nodeId, string variable, double value, string unit)
    {
        var body = $"{tag},{nodeId},{variable},{value:F2},{unit}";
        byte xor = 0;
        foreach (char c in body)
        {
            xor ^= (byte)c;
        }
        var line = $"${body}*{xor:X2}";
        PushLine(line);
        return line;
    }

    /// <summary>
    /// Pushes a formatted JSON telemetry frame.
    /// </summary>
    public string PushJsonFrame(string nodeId, string variable, double value, string unit, long? timestamp = null)
    {
        long ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var json = $"{{\"nodeId\":\"{nodeId}\",\"variable\":\"{variable}\",\"value\":{value:F2},\"unit\":\"{unit}\",\"timestamp\":{ts}}}";
        PushLine(json);
        return json;
    }

    /// <summary>
    /// Pushes a formatted COLUMNS (CSV) telemetry frame.
    /// </summary>
    public string PushColumnsFrame(string nodeId, string variable, double value, string unit, long? timestamp = null)
    {
        long ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var csv = $"{nodeId},{variable},{value:F2},{unit},{ts}";
        PushLine(csv);
        return csv;
    }

    /// <summary>
    /// Generates a continuous synthetic stream of telemetry packets at fixed intervals.
    /// </summary>
    public async Task GenerateSyntheticTelemetryStreamAsync(int packetCount, int intervalMs, CancellationToken cancellationToken = default)
    {
        var random = new Random(42);
        string[] nodes = { "MCU_NODE_1", "MCU_NODE_2" };
        string[] variables = { "TEMP", "VIB", "RPM", "VOLT" };
        string[] units = { "C", "G", "RPM", "V" };

        for (int i = 0; i < packetCount; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            int nodeIdx = i % nodes.Length;
            int varIdx = (i / nodes.Length) % variables.Length;
            double baseVal = varIdx switch
            {
                0 => 45.0 + random.NextDouble() * 30.0, // Temp: 45-75 C
                1 => 0.5 + random.NextDouble() * 3.5,   // Vib: 0.5-4.0 G
                2 => 1200 + random.Next(0, 1800),      // RPM: 1200-3000
                _ => 11.5 + random.NextDouble() * 1.5  // Volt: 11.5-13.0 V
            };

            PushPrefixFrame("TELE", nodes[nodeIdx], variables[varIdx], baseVal, units[varIdx]);

            if (intervalMs > 0)
            {
                await Task.Delay(intervalMs, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Reads and removes all pending lines.
    /// </summary>
    public List<string> ReadAvailableLines()
    {
        var list = new List<string>();
        while (_lineBuffer.TryDequeue(out var line))
        {
            list.Add(line);
        }
        return list;
    }

    /// <summary>
    /// Reads and removes all pending bytes.
    /// </summary>
    public byte[] ReadAvailableBytes()
    {
        var list = new List<byte>();
        while (_byteBuffer.TryDequeue(out var b))
        {
            list.Add(b);
        }
        return list.ToArray();
    }

    /// <summary>
    /// Resets all buffers and connection state.
    /// </summary>
    public void Reset()
    {
        _lineBuffer.Clear();
        _byteBuffer.Clear();
        IsOpen = false;
    }
}
