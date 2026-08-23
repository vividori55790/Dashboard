using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Host.Ingest;
using TelemetryDashboard.Host.Outbound;
using Xunit;
using TelemetryDashboard.Infrastructure.Serial;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The interlock acting on an engineering limit, and the loopback port that makes it observable.
/// </summary>
/// <remarks>
/// Until now the one feature that acts on the machine was armed only on a z-score threshold, and a
/// z-score is blind to exactly the fault an interlock is for: measured on a live host, a channel
/// held 42–119 V above a hard limit for 107 consecutive samples never exceeded 1.94 sigma.
/// <para>
/// Verified on a live loopback run rather than argued: 422 breaching samples produced 10 commands
/// at the port, and the first version of this — which bypassed the controller where the cooldown
/// lives — produced 91 in twenty seconds.
/// </para>
/// </remarks>
public class EmergencyLimitTests
{
    private const string Port = "LOOPBACK";
    private const string Command = "SAFE_MODE";

    private static HostOptions Armed(double cooldownSec, params string[] tripLimits) => new()
    {
        EmergencyStop = true,
        SerialPort = Port,
        EmergencyCommand = Command,
        EmergencyCooldownSec = cooldownSec,
        EmergencySigma = 3.5,
        EmergencyLimits = tripLimits,
        Limits = tripLimits
    };

    private static ScoredSample Sample(double value, params BreachedLimit[] breached) => new(
        Channel: "SIM:COM3.grid.voltage",
        NodeId: "SIM:COM3",
        Variable: "grid.voltage",
        Value: value,
        Unit: "V",
        TimestampUtc: DateTime.UtcNow,
        ZScore: null,
        IsAnomaly: null,
        AnalyzerId: null,
        IsSimulated: true,
        BreachedLimits: breached.Length == 0 ? null : breached);

    private static BreachedLimit Breach(string declaration, bool justEntered) =>
        new(ChannelLimit.Parse(declaration),
            justEntered ? LimitTransition.Entered : LimitTransition.Sustained);

    [Fact]
    public async Task ATripLimitFiresEvenWhenNothingHasBeenScoredYet()
    {
        // The property the sigma path cannot have. During warm-up there is no baseline, so a
        // reading has no z-score at all -- and a reading outside a hard limit is outside it
        // anyway. The machine does not wait for statistics before being damaged.
        var manager = new LoopbackSerialManager();
        await manager.ConnectPortAsync(Port);

        await using EmergencyInterlockRelay relay =
            EmergencyInterlockRelay.Start(Armed(60, "grid.voltage[V] < 300"), manager)!;

        relay.OnSampleScored(null, Sample(384, Breach("grid.voltage[V] < 300", justEntered: true)));

        await WaitForWrites(manager, 1);
        manager.Written.Should().Equal(Command);
        relay.FiredOnLimit.Should().Be(1);
    }

    [Fact]
    public async Task ALimitThatWasNotArmedForTrippingRaisesNoCommand()
    {
        // Acting on the machine is a separate authorisation from raising an alarm, and nothing
        // here can promote one into the other.
        var manager = new LoopbackSerialManager();
        await manager.ConnectPortAsync(Port);

        await using EmergencyInterlockRelay relay =
            EmergencyInterlockRelay.Start(Armed(60, "grid.voltage[V] < 300"), manager)!;

        relay.OnSampleScored(null, Sample(500, Breach("grid.voltage[V] in 320..430", justEntered: true)));

        await Task.Delay(150);
        manager.Written.Should().BeEmpty();
        relay.FiredOnLimit.Should().Be(0);
    }

    [Fact]
    public async Task ASustainedBreachIsNotACommandPerSample()
    {
        // 422 breaching samples produced 10 commands on the live run this pins. Before the
        // cooldown reached the limit path it produced 91 in twenty seconds, which is a flood
        // aimed at the one port that matters.
        var manager = new LoopbackSerialManager();
        await manager.ConnectPortAsync(Port);

        await using EmergencyInterlockRelay relay =
            EmergencyInterlockRelay.Start(Armed(60, "grid.voltage[V] < 300"), manager)!;

        relay.OnSampleScored(null, Sample(384, Breach("grid.voltage[V] < 300", justEntered: true)));
        for (int i = 0; i < 50; i++)
        {
            relay.OnSampleScored(null, Sample(384 + i, Breach("grid.voltage[V] < 300", justEntered: false)));
        }

        await WaitForWrites(manager, 1);
        await Task.Delay(150);

        // The crossing acts; the hold waits for the cooldown.
        manager.Written.Should().Equal(new[] { Command });
        relay.SuppressedOnLimit.Should().Be(50);
    }

    [Fact]
    public async Task AnExcursionThatOutlastsTheCooldownIsAssertedAgain()
    {
        // Both halves are true at once: a command per sample is a flood, and a machine that
        // ignored the first command should be told again.
        var manager = new LoopbackSerialManager();
        await manager.ConnectPortAsync(Port);

        await using EmergencyInterlockRelay relay =
            EmergencyInterlockRelay.Start(Armed(0.2, "grid.voltage[V] < 300"), manager)!;

        relay.OnSampleScored(null, Sample(384, Breach("grid.voltage[V] < 300", justEntered: true)));
        await WaitForWrites(manager, 1);

        await Task.Delay(300);
        relay.OnSampleScored(null, Sample(390, Breach("grid.voltage[V] < 300", justEntered: false)));

        await WaitForWrites(manager, 2);
        manager.Written.Should().HaveCount(2);
    }

    [Fact]
    public void ATripLimitWithoutAnArmedInterlockIsRefusedAtTheCommandLine()
    {
        // A limit that says it will act, on a host that cannot, is worse than no limit: it reads
        // as protection.
        HostOptions parsed = CommandLineParser.Parse(
            new[] { "--simulate", "--emergency-limit", "grid.voltage[V] < 300" }, new HostOptions());

        parsed.Error.Should().Contain("--emergency-stop");
    }

    [Fact]
    public void ATripLimitIsAlsoAnOrdinaryLimit()
    {
        // It appears in /api/limits, raises the same alarm and carries the same unit check. The
        // flag adds an action; it does not create a second rule set.
        HostOptions parsed = CommandLineParser.Parse(
            new[] { "--serial", "loopback", "--emergency-stop",
                    "--emergency-limit", "grid.voltage[V] < 300" },
            new HostOptions());

        parsed.Error.Should().BeNull();
        parsed.Limits.Should().Contain("grid.voltage[V] < 300");
        parsed.EmergencyLimits.Should().Contain("grid.voltage[V] < 300");
    }

    // ---- the loopback port -------------------------------------------------

    [Fact]
    public async Task AFramePushedIntoTheLoopbackComesBackOutOfIt()
    {
        // Through the port's own buffer rather than around it, so the parser downstream sees what
        // a device's bytes would produce.
        var manager = new LoopbackSerialManager();
        await manager.ConnectPortAsync(Port);

        manager.Deliver(Port, "$TELE,COM3,grid.voltage,380.00,V*4A").Should().BeTrue();

        manager.PacketReader.TryRead(out RawPacket packet).Should().BeTrue();
        packet.PortName.Should().Be(Port);
        packet.RawLine.Should().Be("$TELE,COM3,grid.voltage,380.00,V*4A");
    }

    [Fact]
    public async Task WritingToAPortThatIsNotOpenIsAFailureRatherThanANoOp()
    {
        // Swallowing it would let an interlock report a dispatch to a port that was never open,
        // which is the most expensive lie this host could tell.
        var manager = new LoopbackSerialManager();

        Func<Task> write = () => manager.WriteLineAsync(Port, Command);

        await write.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not connected*");
        manager.WriteCount.Should().Be(0);
    }

    [Fact]
    public void ALoopbackRunIsMarkedSimulated()
    {
        // The frames are generated. A loopback run reporting REAL_HARDWARE would put synthetic
        // readings into the archive under a name saying a machine produced them.
        var source = new LoopbackTelemetrySource();

        source.IsSimulated.Should().BeTrue();
        source.Origin.Should().Be("SIMULATED");
    }

    private static async Task WaitForWrites(LoopbackSerialManager manager, int count)
    {
        // The relay hands the command to a bounded queue that writes on its own loop, so the write
        // is not synchronous with the decision -- deliberately, since a wedged port must not stall
        // ingest.
        for (int i = 0; i < 100 && manager.WriteCount < count; i++) await Task.Delay(20);
        manager.WriteCount.Should().BeGreaterThanOrEqualTo(count, "the command should have reached the port");
    }
}
