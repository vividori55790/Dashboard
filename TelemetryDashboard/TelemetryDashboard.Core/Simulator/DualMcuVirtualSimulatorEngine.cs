namespace TelemetryDashboard.Core.Simulator;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Parsers;

/// <summary>
/// Dual-MCU Virtual Simulator Engine producing synthetic thermal, vibration, RPM, voltage waveforms,
/// $HEX raw binary frames, and $HIST historical snapshots across COM3 and COM4 streams.
/// </summary>
public sealed class DualMcuVirtualSimulatorEngine : ISimulatorEngine
{
    private readonly Channel<RawPacket> _channel;
    private CancellationTokenSource? _cts;
    private Task? _simulationTask;
    private readonly object _stateLock = new();

    public bool IsRunning { get; private set; }

    public DualMcuVirtualSimulatorEngine()
    {
        BoundedChannelOptions options = new(capacity: 10_000)
        {
            SingleWriter = false,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.DropOldest
        };
        _channel = Channel.CreateBounded<RawPacket>(options);
    }

    public void StartSimulation()
    {
        lock (_stateLock)
        {
            if (IsRunning) return;
            IsRunning = true;
            _cts = new CancellationTokenSource();
            _simulationTask = Task.Run(() => GenerateLoopAsync(_cts.Token));
        }
    }

    public void StopSimulation()
    {
        lock (_stateLock)
        {
            if (!IsRunning) return;
            IsRunning = false;
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException) { }
            
            _cts?.Dispose();
            _cts = null;
        }
    }

    public async IAsyncEnumerable<RawPacket> StreamSimulatedPackets([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out RawPacket packet))
            {
                yield return packet;
            }
        }
    }

    private async Task GenerateLoopAsync(CancellationToken cancellationToken)
    {
        Random random = new(42);
        double step = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            step += 0.1;
            DateTime now = DateTime.UtcNow;

            // MCU Node 1 (COM3): Thermal & Vibration
            double temp1 = 50.0 + 15.0 * Math.Sin(2 * Math.PI * 0.05 * step) + (random.NextDouble() * 2 - 1) * 1.5;
            double vib1 = 1.2 + 0.8 * Math.Sin(2 * Math.PI * 10.0 * step) + 0.5 * Math.Cos(2 * Math.PI * 25.0 * step) + (random.NextDouble() * 2 - 1) * 0.2;

            string p1Body = $"TELE,MCU_NODE_1,TEMP,{temp1:F2},C";
            byte cs1 = XorChecksum.Calculate(Encoding.UTF8.GetBytes(p1Body));
            string p1 = $"${p1Body}*{cs1:X2}";

            string p2Body = $"TELE,MCU_NODE_1,VIB,{vib1:F2},G";
            byte cs2 = XorChecksum.Calculate(Encoding.UTF8.GetBytes(p2Body));
            string p2 = $"${p2Body}*{cs2:X2}";

            // MCU Node 2 (COM4): RPM & Voltage, $HEX, $HIST
            double rpm2 = 1800 + 400 * Math.Sin(2 * Math.PI * 0.1 * step) + (random.NextDouble() * 2 - 1) * 25.0;
            double volt2 = 12.0 + 0.3 * Math.Sin(2 * Math.PI * 0.02 * step) + (random.NextDouble() * 2 - 1) * 0.05;

            string p3Body = $"TELE,MCU_NODE_2,RPM,{rpm2:F0},RPM";
            byte cs3 = XorChecksum.Calculate(Encoding.UTF8.GetBytes(p3Body));
            string p3 = $"${p3Body}*{cs3:X2}";

            string p4Body = $"TELE,MCU_NODE_2,VOLT,{volt2:F2},V";
            byte cs4 = XorChecksum.Calculate(Encoding.UTF8.GetBytes(p4Body));
            string p4 = $"${p4Body}*{cs4:X2}";

            // Raw $HEX frame
            string hexBody = "HEX,MCU_NODE_2,414243313233";
            byte csHex = XorChecksum.Calculate(Encoding.UTF8.GetBytes(hexBody));
            string pHex = $"${hexBody}*{csHex:X2}";

            // Historical $HIST frame
            long unixTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string pHist = $"$HIST,MCU_NODE_2,TEMP,{temp1:F2},{unixTs}";

            _channel.Writer.TryWrite(new RawPacket("COM3", p1, now));
            _channel.Writer.TryWrite(new RawPacket("COM3", p2, now));
            _channel.Writer.TryWrite(new RawPacket("COM4", p3, now));
            _channel.Writer.TryWrite(new RawPacket("COM4", p4, now));
            _channel.Writer.TryWrite(new RawPacket("COM4", pHex, now));
            _channel.Writer.TryWrite(new RawPacket("COM4", pHist, now));

            try
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        StopSimulation();
    }

    public ValueTask DisposeAsync()
    {
        StopSimulation();
        return ValueTask.CompletedTask;
    }
}
