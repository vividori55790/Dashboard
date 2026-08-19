namespace TelemetryDashboard.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Events;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Serial;
using Xunit;

public class EmpiricalAutoBaudScannerTests
{
    private class MockScannerSerialManager : ISerialManager
    {
        private readonly Dictionary<int, List<string>> _baudPayloads = new();
        private readonly HashSet<int> _failingConnectBauds = new();
        private Channel<RawPacket> _channel = Channel.CreateUnbounded<RawPacket>();
        private readonly Dictionary<string, PortConnectionStatus> _statuses = new();

        public ChannelReader<RawPacket> PacketReader => _channel.Reader;
        public IReadOnlyDictionary<string, PortConnectionStatus> ActivePorts => _statuses;
        // Required by ISerialManager; this double never raises it. Suppressed rather than removed
        // because the interface would not be satisfied without it, and an unexplained warning in
        // the build output is where real ones go to hide.
#pragma warning disable CS0067
        public event EventHandler<DeviceChangeEventArgs>? DeviceChanged;
#pragma warning restore CS0067

        public int CurrentlyConnectedBaud { get; private set; } = 0;
        public List<int> TriedBaudRates { get; } = new();

        public void SetPayloadsForBaud(int baudRate, IEnumerable<string> lines)
        {
            _baudPayloads[baudRate] = lines.ToList();
        }

        public void SetConnectFailureForBaud(int baudRate)
        {
            _failingConnectBauds.Add(baudRate);
        }

        public Task<bool> ConnectPortAsync(string portName, int baudRate = 115200, CancellationToken cancellationToken = default)
        {
            return ConnectAsync(portName, baudRate);
        }

        public Task<bool> ConnectAsync(string portName, int baudRate)
        {
            TriedBaudRates.Add(baudRate);
            if (_failingConnectBauds.Contains(baudRate))
            {
                return Task.FromResult(false);
            }

            CurrentlyConnectedBaud = baudRate;
            _statuses[portName] = PortConnectionStatus.Connected;

            // Re-create channel to clear old packets from previous connects
            _channel = Channel.CreateUnbounded<RawPacket>();

            if (_baudPayloads.TryGetValue(baudRate, out var lines))
            {
                foreach (var line in lines)
                {
                    _channel.Writer.TryWrite(new RawPacket(portName, line, DateTime.UtcNow));
                }
            }

            return Task.FromResult(true);
        }

        public Task DisconnectPortAsync(string portName)
        {
            _statuses[portName] = PortConnectionStatus.Disconnected;
            CurrentlyConnectedBaud = 0;
            return Task.CompletedTask;
        }

        public Task DisconnectAllAsync()
        {
            _statuses.Clear();
            CurrentlyConnectedBaud = 0;
            return Task.CompletedTask;
        }

        public Task WriteLineAsync(string portName, string data, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    [Theory]
    [InlineData(9600, "$TELE,MCU1,TEMP,25.0*1A", PacketFormat.Prefix)]
    [InlineData(115200, "{\"nodeId\":\"MCU1\",\"temp\":36.5}", PacketFormat.Json)]
    [InlineData(921600, "$HEX,MCU1,414243313233*4B", PacketFormat.Hex)]
    public async Task AutoBaudScanner_StandardAndHighSpeedScanning_DetectsCorrectBaudAndFormat(
        int targetBaud, string validPayload, PacketFormat expectedFormat)
    {
        var mockManager = new MockScannerSerialManager();
        mockManager.SetPayloadsForBaud(targetBaud, new[] { validPayload });

        var scanner = new AutoBaudScanner(mockManager);
        var result = await scanner.ScanAsync("COM3");

        result.IsSuccess.Should().BeTrue($"Scanner should successfully scan baud rate {targetBaud}");
        result.DetectedBaudRate.Should().Be(targetBaud);
        result.DetectedFormat.Should().Be(expectedFormat);
        result.PortName.Should().Be("COM3");
    }

    [Fact]
    public async Task AutoBaudScanner_CustomCandidateBaudRates_OrderPreservation()
    {
        var mockManager = new MockScannerSerialManager();
        int[] customCandidates = new[] { 921600, 115200, 9600 };

        // Set valid payload ONLY at 9600 (the last candidate)
        mockManager.SetPayloadsForBaud(9600, new[] { "MCU1,TEMP,45.0,C" });

        var scanner = new AutoBaudScanner(mockManager);
        var result = await scanner.ScanAsync("COM5", customCandidates);

        result.IsSuccess.Should().BeTrue();
        result.DetectedBaudRate.Should().Be(9600);
        result.DetectedFormat.Should().Be(PacketFormat.Columns);

        // Verify order of attempted baud rates matches customCandidates order
        mockManager.TriedBaudRates.Should().ContainInConsecutiveOrder(921600, 115200, 9600);
    }

    [Fact]
    public async Task AutoBaudScanner_NoiseOnWrongBaud_SkipsAndLocksOntoValidBaud()
    {
        var mockManager = new MockScannerSerialManager();

        // 9600 emits noisy garbage with null bytes / control characters
        mockManager.SetPayloadsForBaud(9600, new[] { "\0\u0001\u0002GARBAGE\u001F", "BAD_LINE" });

        // 19200 emits nothing (timeout)
        // 115200 emits clean Prefix telemetry frame
        mockManager.SetPayloadsForBaud(115200, new[] { "$TELE,MCU2,RPM,1500*32" });

        var scanner = new AutoBaudScanner(mockManager);
        var result = await scanner.ScanAsync("COM4");

        result.IsSuccess.Should().BeTrue("Scanner should skip noisy 9600 and silent 19200 to lock onto 115200");
        result.DetectedBaudRate.Should().Be(115200);
        result.DetectedFormat.Should().Be(PacketFormat.Prefix);
    }

    [Fact]
    public async Task AutoBaudScanner_ConnectFailure_ContinuesToNextCandidate()
    {
        var mockManager = new MockScannerSerialManager();
        mockManager.SetConnectFailureForBaud(9600); // 9600 connect fails
        mockManager.SetConnectFailureForBaud(19200); // 19200 connect fails

        mockManager.SetPayloadsForBaud(38400, new[] { "{\"sensor\":\"vibe\",\"val\":0.05}" });

        var scanner = new AutoBaudScanner(mockManager);
        var result = await scanner.ScanAsync("COM1");

        result.IsSuccess.Should().BeTrue();
        result.DetectedBaudRate.Should().Be(38400);
        result.DetectedFormat.Should().Be(PacketFormat.Json);
    }

    [Fact]
    public async Task AutoBaudScanner_CancellationToken_AbortsScanningImmediately()
    {
        var mockManager = new MockScannerSerialManager();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-canceled

        var scanner = new AutoBaudScanner(mockManager);
        var result = await scanner.ScanAsync("COM3", cancellationToken: cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.DetectedBaudRate.Should().Be(0);
        result.DetectedFormat.Should().Be(PacketFormat.Unknown);
        mockManager.TriedBaudRates.Should().BeEmpty();
    }

    [Fact]
    public void AutoBaudScanner_DetectFormat_ByteStreamHeuristicsAndGarbageRejection()
    {
        var scanner = new AutoBaudScanner(null!);

        // Null bytes or control chars below 0x09 or between 0x0E-0x1F or 0x7F
        byte[] nullByteNoise = Encoding.UTF8.GetBytes("DATA\0123");
        scanner.DetectFormat(nullByteNoise).Should().Be(PacketFormat.Unknown, "Null bytes represent baud mismatch garbage");

        byte[] controlCharNoise = new byte[] { 0x05, (byte)'$', (byte)'T', (byte)'E', (byte)'L', (byte)'E' };
        scanner.DetectFormat(controlCharNoise).Should().Be(PacketFormat.Unknown, "Leading control char should be classified Unknown");

        byte[] validUtf8Bytes = Encoding.UTF8.GetBytes("$TELE,MCU1,TEMP,50.0*12");
        scanner.DetectFormat(validUtf8Bytes).Should().Be(PacketFormat.Prefix);

        // Null or empty byte array
        scanner.DetectFormat((byte[]?)null).Should().Be(PacketFormat.Unknown);
        scanner.DetectFormat(Array.Empty<byte>()).Should().Be(PacketFormat.Unknown);
    }

    [Theory]
    [InlineData("$HEX,MCU1,414243*12", PacketFormat.Hex)]
    [InlineData("$hex,MCU1,414243*12", PacketFormat.Hex)] // Case insensitivity check
    [InlineData("$TELE,MCU1,TEMP,45.0*12", PacketFormat.Prefix)]
    [InlineData("{\"nodeId\":\"MCU1\",\"temp\":45.0}", PacketFormat.Json)]
    [InlineData("MCU1,TEMP,45.0,C", PacketFormat.Columns)]
    [InlineData("  {\"nodeId\":\"MCU1\",\"temp\":45.0}  ", PacketFormat.Json)] // Whitespace trimming
    [InlineData("MCU1,TEMP", PacketFormat.Unknown)] // Only 2 columns
    [InlineData("{\"unclosed_json", PacketFormat.Unknown)]
    [InlineData("", PacketFormat.Unknown)]
    [InlineData("   ", PacketFormat.Unknown)]
    [InlineData(null, PacketFormat.Unknown)]
    public void AutoBaudScanner_DetectFormat_StringHeuristics(string? line, PacketFormat expectedFormat)
    {
        var scanner = new AutoBaudScanner(null!);
        PacketFormat format = scanner.DetectFormat(line);
        format.Should().Be(expectedFormat);
    }
}
