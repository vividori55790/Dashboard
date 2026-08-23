using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Events;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Host.Outbound;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The one path in this host that writes to hardware instead of reading it.
/// </summary>
/// <remarks>
/// <c>EmergencyMcuController</c> was Feature 12, marked Built since M3, and constructed by nothing,
/// so the interlock could not fire in any running program. Wiring it raised a safety question the
/// tests below pin: what must be true before this host is allowed to transmit.
/// <para>
/// The byte actually leaving a serial port is not verified here or anywhere — that needs hardware
/// this repository does not have. What is verified is every decision in front of it: when the
/// interlock arms, when it refuses to arm, what it sends, where it sends it, and what it does with
/// the triggers it holds back.
/// </para>
/// </remarks>
public class EmergencyInterlockTests
{
    /// <summary>Records what would have gone to a port, so the decision can be checked without one.</summary>
    private sealed class CapturingSerialManager : ISerialManager
    {
        private readonly ConcurrentDictionary<string, PortConnectionStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);

        public ConcurrentQueue<(string Port, string Data)> Writes { get; } = new();

        public System.Threading.Channels.ChannelReader<RawPacket> PacketReader =>
            throw new NotSupportedException("The interlock never reads; it only writes.");

        public IReadOnlyDictionary<string, PortConnectionStatus> ActivePorts => _statuses;

        /// <summary>Required by the interface. This fake never raises it, so it holds no field.</summary>
        public event EventHandler<DeviceChangeEventArgs>? DeviceChanged { add { } remove { } }
        public event EventHandler<TelemetryDashboard.Core.Events.SerialPortFaultEventArgs>? PortFaulted { add { } remove { } }
        public event EventHandler<string>? PortRecovered { add { } remove { } }

        public Task<bool> ConnectPortAsync(string portName, int baudRate = 115200, CancellationToken cancellationToken = default)
        {
            _statuses[portName] = PortConnectionStatus.Connected;
            return Task.FromResult(true);
        }

        public Task DisconnectPortAsync(string portName)
        {
            _statuses.TryRemove(portName, out _);
            return Task.CompletedTask;
        }

        public Task DisconnectAllAsync()
        {
            _statuses.Clear();
            return Task.CompletedTask;
        }

        public Task WriteLineAsync(string portName, string data, CancellationToken cancellationToken = default)
        {
            Writes.Enqueue((portName, data));
            return Task.CompletedTask;
        }

        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static HostOptions Armed(double sigma = 3.5, double cooldown = 5.0, string? command = null) => new()
    {
        SerialPort = "COM7",
        EmergencyStop = true,
        EmergencySigma = sigma,
        EmergencyCooldownSec = cooldown,
        EmergencyCommand = command ?? HostOptions.DefaultEmergencyCommand
    };

    private static ScoredSample Sample(double value, double? z) => new(
        "NODE.temp", "NODE", "temp", value, "C", DateTime.UtcNow, z, z >= 3.5, "test", false);

    /// <summary>Fires the sample and drains the transmit queue, so the assertion is not a race.</summary>
    private static async Task<CapturingSerialManager> RunAsync(
        HostOptions options, params ScoredSample[] samples)
    {
        var serial = new CapturingSerialManager();
        EmergencyInterlockRelay? relay = EmergencyInterlockRelay.Start(options, serial);
        relay.Should().NotBeNull();

        foreach (ScoredSample sample in samples) relay!.OnSampleScored(null, sample);

        // Disposing drains the queue, which is what makes this deterministic rather than timed.
        await relay!.DisposeAsync();
        return serial;
    }

    [Fact]
    public void TheInterlockDoesNotArmUnlessItWasAskedFor()
    {
        var off = new HostOptions { SerialPort = "COM7", EmergencyStop = false };

        EmergencyInterlockRelay.Start(off, new CapturingSerialManager()).Should().BeNull(
            "a monitoring tool that transmits to hardware without being told to is not a feature");
    }

    [Fact]
    public void TheInterlockDoesNotArmWithoutAPortToWriteTo()
    {
        var noPort = new HostOptions { SerialPort = null, EmergencyStop = true };

        EmergencyInterlockRelay.Start(noPort, new CapturingSerialManager()).Should().BeNull();
        EmergencyInterlockRelay.Start(Armed(), serialManager: null).Should().BeNull();
    }

    [Fact]
    public async Task ASampleWithNoVerdictNeverFires()
    {
        // Warm-up. The engine reports no z-score at all until it has a baseline, and treating that
        // as a small number would let the first readings of a run trip an interlock on a machine
        // nobody has measured yet.
        CapturingSerialManager serial = await RunAsync(Armed(), Sample(value: 9_999, z: null));

        serial.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ASampleBelowTheThresholdNeverFires()
    {
        CapturingSerialManager serial = await RunAsync(Armed(sigma: 3.5), Sample(value: 30, z: 3.4));

        serial.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ASampleAboveTheThresholdTransmitsTheConfiguredCommandToTheConfiguredPort()
    {
        CapturingSerialManager serial = await RunAsync(
            Armed(sigma: 3.5, command: "$CMD,HALT"), Sample(value: 120, z: 4.1));

        serial.Writes.Should().ContainSingle();
        serial.Writes.Single().Port.Should().Be("COM7",
            "the interlock writes to the port the operator opened, never to the controller's COM3 default");
        serial.Writes.Single().Data.Should().Be("$CMD,HALT");
    }

    [Fact]
    public async Task AStormOfTriggersBecomesOneDispatchAndTheRestAreCounted()
    {
        var serial = new CapturingSerialManager();
        EmergencyInterlockRelay? relay = EmergencyInterlockRelay.Start(Armed(cooldown: 60), serial);

        for (int i = 0; i < 20; i++) relay!.OnSampleScored(null, Sample(value: 120 + i, z: 4.1));

        long fired = relay!.Fired;
        long suppressed = relay.SuppressedByCooldown;
        await relay.DisposeAsync();

        fired.Should().Be(1, "the cooldown collapses a storm into one command");
        suppressed.Should().Be(19,
            "what was held back is counted; a reader who saw one dispatch and nothing else could "
            + "not tell a throttled storm from a condition that cleared");
        serial.Writes.Should().ContainSingle();
    }

    [Fact]
    public async Task TheInterlockFiresAgainOnceTheCooldownHasPassed()
    {
        var serial = new CapturingSerialManager();
        EmergencyInterlockRelay? relay = EmergencyInterlockRelay.Start(Armed(cooldown: 0), serial);

        relay!.OnSampleScored(null, Sample(value: 120, z: 4.1));
        relay.OnSampleScored(null, Sample(value: 121, z: 4.2));
        await relay.DisposeAsync();

        // Cooldown 0 means no throttling at all, which is the boundary of the rule above.
        serial.Writes.Should().HaveCount(2);
    }

    [Fact]
    public async Task NothingIsReportedWhenTheInterlockNeverFired()
    {
        var serial = new CapturingSerialManager();
        EmergencyInterlockRelay? relay = EmergencyInterlockRelay.Start(Armed(), serial);

        relay!.OnSampleScored(null, Sample(value: 30, z: 1.0));
        string? summary = relay.Summary();
        await relay.DisposeAsync();

        summary.Should().BeNull("a quiet run has nothing to say, and saying something anyway is noise");
    }

    [Fact]
    public async Task AFiredInterlockIsReportedWithWhatItSentAndWhatItHeldBack()
    {
        var serial = new CapturingSerialManager();
        EmergencyInterlockRelay? relay = EmergencyInterlockRelay.Start(Armed(cooldown: 60), serial);

        for (int i = 0; i < 5; i++) relay!.OnSampleScored(null, Sample(value: 120, z: 4.1));
        string? summary = relay!.Summary();
        await relay.DisposeAsync();

        summary.Should().NotBeNull();
        summary.Should().Contain("COM7").And.Contain("1 dispatch").And.Contain("4 suppressed");
    }
}
