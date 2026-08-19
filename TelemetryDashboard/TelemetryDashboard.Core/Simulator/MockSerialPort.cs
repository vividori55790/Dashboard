namespace TelemetryDashboard.Core.Simulator;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Parsers;

/// <summary>
/// In-memory mock serial port stream wrapper used for hardware-decoupled testing and virtual MCU simulation.
/// </summary>
public class MockSerialPort
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

    public MockSerialPort(string portName = "COM3", int baudRate = 115200)
    {
        PortName = portName;
        BaudRate = baudRate;
        IsOpen = false;
    }

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

    public void PushLine(string line)
    {
        if (line == null) return;
        _lineBuffer.Enqueue(line);

        byte[] bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        foreach (byte b in bytes)
        {
            _byteBuffer.Enqueue(b);
        }

        if (IsOpen)
        {
            LineReceived?.Invoke(this, line);
            DataReceived?.Invoke(this, bytes);
        }
    }

    public void PushBytes(byte[] data)
    {
        if (data == null || data.Length == 0) return;
        foreach (byte b in data)
        {
            _byteBuffer.Enqueue(b);
        }

        if (IsOpen)
        {
            DataReceived?.Invoke(this, data);
            string str = Encoding.UTF8.GetString(data);
            if (str.Contains('\n') || str.Contains('\r'))
            {
                string[] lines = str.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string l in lines)
                {
                    _lineBuffer.Enqueue(l);
                    LineReceived?.Invoke(this, l);
                }
            }
        }
    }

    public string PushPrefixFrame(string tag, string nodeId, string variable, double value, string unit)
    {
        string body = $"{tag},{nodeId},{variable},{value:F2},{unit}";
        byte xor = XorChecksum.Calculate(Encoding.UTF8.GetBytes(body));
        string line = $"${body}*{xor:X2}";
        PushLine(line);
        return line;
    }

    public List<string> ReadAvailableLines()
    {
        List<string> list = new();
        while (_lineBuffer.TryDequeue(out string? line))
        {
            list.Add(line);
        }
        return list;
    }

    public byte[] ReadAvailableBytes()
    {
        List<byte> list = new();
        while (_byteBuffer.TryDequeue(out byte b))
        {
            list.Add(b);
        }
        return list.ToArray();
    }

    public void Reset()
    {
        _lineBuffer.Clear();
        _byteBuffer.Clear();
        IsOpen = false;
    }
}
