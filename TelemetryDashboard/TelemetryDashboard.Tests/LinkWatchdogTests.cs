using TelemetryDashboard.Core.Events;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Serial;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Keeping a serial link up without anybody watching it.
/// </summary>
/// <remarks>
/// The engine existed for a long time and the desktop shell never used it, so a bumped USB cable
/// ended a session in the quietest way available: the charts stopped, the indicator went on saying
/// connected, and the only evidence was the silence watch reporting channel after channel going
/// quiet — which reads like a machine shutting down rather than a cable coming loose.
/// <para>
/// The loop's decision is "the port is present and we are not on it", which used to be taken
/// against <c>SerialPort.GetPortNames</c> directly. That made the one thing worth testing untestable
/// anywhere except on a bench with real hardware, so the enumeration is injected here — the same
/// seam <see cref="PortPresencePoller"/> already had.
/// </para>
/// </remarks>
public class LinkWatchdogTests
{
    /// <summary>A manager that can be told what happens when the port is opened.</summary>
    private sealed class FakeManager : ISerialManager
    {
        private readonly System.Threading.Channels.Channel<RawPacket> _channel =
            System.Threading.Channels.Channel.CreateUnbounded<RawPacket>();

        public Func<string, bool> OnConnect { get; set; } = _ => true;
        public List<string> Written { get; } = [];
        public Dictionary<string, PortConnectionStatus> Ports { get; } = [];

        public System.Threading.Channels.ChannelReader<RawPacket> PacketReader => _channel.Reader;
        public IReadOnlyDictionary<string, PortConnectionStatus> ActivePorts => Ports;
        // Never raised: this fake exists to exercise the periodic loop, which is the path the
        // desktop depends on. Windows raises device changes through a message pump the engine
        // cannot assume exists.
#pragma warning disable CS0067
        public event EventHandler<DeviceChangeEventArgs>? DeviceChanged;

#pragma warning disable CS0067
        public event EventHandler<TelemetryDashboard.Core.Events.SerialPortFaultEventArgs>? PortFaulted;
        public event EventHandler<string>? PortRecovered;
#pragma warning restore CS0067
#pragma warning restore CS0067

        public Task<bool> ConnectPortAsync(string portName, int baudRate = 115200, CancellationToken token = default)
        {
            bool ok = OnConnect(portName);
            Ports[portName] = ok ? PortConnectionStatus.Connected : PortConnectionStatus.Faulted;
            return Task.FromResult(ok);
        }

        public Task DisconnectPortAsync(string portName)
        {
            Ports.Remove(portName);
            return Task.CompletedTask;
        }

        public Task DisconnectAllAsync() { Ports.Clear(); return Task.CompletedTask; }

        public Task WriteLineAsync(string portName, string data, CancellationToken token = default)
        {
            lock (Written) Written.Add(data);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private static async Task<bool> Within(TimeSpan limit, Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }

        return condition();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task APortThatComesBackIsOpenedAndAskedToResend()
    {
        var manager = new FakeManager();
        string[] present = [];
        await using var engine = new AutoReconnectEngine(
            manager, TimeSpan.FromMilliseconds(30), () => present);

        PortLinkEventArgs? opened = null;
        engine.Reconnected += (_, e) => opened = e;
        engine.StartMonitoring("COM9", 115200);

        (await Within(TimeSpan.FromSeconds(1), () => opened is not null))
            .Should().BeFalse("nothing should be opened while the port is not there");

        present = ["COM9"];

        (await Within(TimeSpan.FromSeconds(3), () => opened is not null)).Should().BeTrue();
        opened!.PortName.Should().Be("COM9");
        manager.Written.Should().ContainSingle().Which.Should().StartWith("$CMD,REQ_RESYNC,");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task AnAttemptThatThrowsDoesNotEndTheWatch()
    {
        // The defect this cost a live run to find. Opening a port somebody else holds throws rather
        // than returning false, and one throw left the monitor loop -- which catches only
        // cancellation -- so the task ended, nothing was awaiting it, and the watchdog went quiet
        // for the rest of the session. Releasing the port afterwards reconnected nothing.
        var manager = new FakeManager { OnConnect = _ => throw new UnauthorizedAccessException("busy") };
        await using var engine = new AutoReconnectEngine(
            manager, TimeSpan.FromMilliseconds(30), () => ["COM9"]);

        int failures = 0;
        PortLinkEventArgs? opened = null;
        engine.ReconnectFailed += (_, _) => Interlocked.Increment(ref failures);
        engine.Reconnected += (_, e) => opened = e;
        engine.StartMonitoring("COM9", 115200);

        (await Within(TimeSpan.FromSeconds(2), () => failures >= 3))
            .Should().BeTrue("the watch has to survive an attempt that throws");

        manager.OnConnect = _ => true;

        (await Within(TimeSpan.FromSeconds(3), () => opened is not null))
            .Should().BeTrue("and it has to still be watching when the port frees up");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task TheResyncAsksFromTheLastReadingRatherThanFromTheStartOfTheSession()
    {
        // UpdateLastTimestamp had no caller anywhere in the product, so this number stayed at the
        // moment the port was registered. A link that dropped after eight hours asked the device to
        // resend eight hours of history, which on a bench link is a second outage rather than a
        // recovery.
        var manager = new FakeManager { OnConnect = _ => false };
        string[] present = ["COM9"];
        await using var engine = new AutoReconnectEngine(
            manager, TimeSpan.FromMilliseconds(30), () => present);

        engine.RegisterTargetPort("COM9", 115200, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        engine.UpdateLastTimestamp("COM9", new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc));

        PortLinkEventArgs? opened = null;
        engine.Reconnected += (_, e) => opened = e;
        manager.OnConnect = _ => true;
        engine.Start();

        (await Within(TimeSpan.FromSeconds(3), () => opened is not null)).Should().BeTrue();
        opened!.ResyncFromUtc.Should().Be(new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc));
        manager.Written[0].Should().Contain("2026-01-01T08:00:00.000Z");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task APortAlreadyConnectedIsLeftAlone()
    {
        var manager = new FakeManager();
        manager.Ports["COM9"] = PortConnectionStatus.Connected;
        await using var engine = new AutoReconnectEngine(
            manager, TimeSpan.FromMilliseconds(30), () => ["COM9"]);

        bool touched = false;
        engine.Reconnected += (_, _) => touched = true;
        engine.StartMonitoring("COM9", 115200);

        (await Within(TimeSpan.FromSeconds(1), () => touched))
            .Should().BeFalse("reopening a working link would drop the data crossing it");
    }
}
