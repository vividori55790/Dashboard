using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Recording;
using TelemetryDashboard.Core.Streaming;
using TelemetryDashboard.Host.Ingest;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Publishing derived channels onto the live path, and the instant they are computed for.
/// </summary>
/// <remarks>
/// The choice of instant is the whole design. "Now" refuses everything, because an input other
/// than the one that just arrived has nothing after now and could only be held. The oldest of the
/// newest samples is the latest instant every input can be interpolated at — and it also sets the
/// rate, so a derived channel cannot be published faster than its slowest input.
/// </remarks>
public class ComputedChannelPumpTests
{
    private const double T0 = 1_000_000.0;

    private sealed class Harness
    {
        public readonly TelemetryStreamingServer Server = new(port: 0);
        public readonly List<TelemetryPacket> Published = new();
        public readonly ComputedChannelPump Pump;

        public Harness(params string[] declarations)
        {
            Server.Computed = declarations.Select(ComputedChannel.Parse).ToList();
            Pump = new ComputedChannelPump(Server, (packet, _, _) =>
            {
                Published.Add(packet);

                // The real publisher writes the sample into the series store on its way to the
                // wire, so a later expression can read an earlier derived channel. Mirrored here,
                // or the chaining test would be measuring a harness that does not do what the
                // host does.
                Server.Series.Append(
                    packet.NodeId + "." + packet.Variable,
                    packet.Value,
                    (packet.Timestamp - DateTime.UnixEpoch).TotalSeconds);

                return ValueTask.CompletedTask;
            });
        }

        public void Sample(string channel, double value, double atSec) =>
            Server.Series.Append(channel, value, atSec);

        public Task Tick() => Pump.TickAsync(CancellationToken.None).AsTask();
    }

    [Fact]
    public async Task NothingIsPublishedUntilEveryInputHasSpoken()
    {
        var h = new Harness("p[W] = a.v * a.i");
        h.Sample("N.a.v", 400, T0);

        await h.Tick();

        h.Published.Should().BeEmpty("one input having reported is not an instant both can answer");
    }

    [Fact]
    public async Task TheInstantIsTheOldestOfTheNewestSamples()
    {
        var h = new Harness("p[W] = a.v * a.i");
        h.Sample("N.a.v", 400, T0);
        h.Sample("N.a.v", 400, T0 + 1);
        h.Sample("N.a.v", 400, T0 + 2);      // fast input, newest at T0+2
        h.Sample("N.a.i", 25, T0);
        h.Sample("N.a.i", 25, T0 + 1);       // slow input, newest at T0+1

        await h.Tick();

        TelemetryPacket published = h.Published.Should().ContainSingle().Subject;
        (published.Timestamp - DateTime.UnixEpoch).TotalSeconds.Should().Be(T0 + 1,
            "at T0+2 the current has nothing after it and could only be held");
        published.Value.Should().Be(10_000.0);
    }

    [Fact]
    public async Task ThePublishedSampleSaysItWasDerivedAndCarriesItsUnit()
    {
        var h = new Harness("p[W] = a.v * a.i");
        h.Sample("N.a.v", 400, T0);
        h.Sample("N.a.i", 25, T0);

        await h.Tick();

        TelemetryPacket published = h.Published.Single();
        published.Flags.HasFlag(PacketFlags.IsDerived).Should().BeTrue(
            "a computed value that cannot say so is read as a measurement");
        published.Unit.Should().Be("W");
        published.NodeId.Should().Be(ComputedChannelPump.NodeId);
        published.Variable.Should().Be("p");
    }

    [Fact]
    public async Task TheRateIsTheSlowestInputsRateRatherThanTheTickRate()
    {
        // Ten ticks over a stream where the current arrives once a second and the voltage ten
        // times. A pump driven by arrivals, or by its own clock, would publish ten values a second
        // out of one current sample — interpolated detail that looks like resolution.
        var h = new Harness("p[W] = a.v * a.i");

        for (int i = 0; i <= 20; i++) h.Sample("N.a.v", 400 + i, T0 + i * 0.1);
        h.Sample("N.a.i", 25, T0);
        h.Sample("N.a.i", 26, T0 + 1);
        h.Sample("N.a.i", 27, T0 + 2);

        for (int tick = 0; tick < 10; tick++) await h.Tick();

        // One value, at T0+2: the only instant both inputs reach, published once.
        h.Published.Select(p => (p.Timestamp - DateTime.UnixEpoch).TotalSeconds)
            .Should().Equal(new[] { T0 + 2 });
    }

    [Fact]
    public async Task AnInstantIsNeverPublishedTwice()
    {
        var h = new Harness("p[W] = a.v * a.i");
        h.Sample("N.a.v", 400, T0);
        h.Sample("N.a.i", 25, T0);

        await h.Tick();
        await h.Tick();
        await h.Tick();

        h.Published.Should().HaveCount(1);
    }

    [Fact]
    public async Task AnInputThatFallsSilentStopsTheChannel()
    {
        var h = new Harness("p[W] = a.v * a.i");
        h.Sample("N.a.v", 400, T0);
        h.Sample("N.a.i", 25, T0);
        await h.Tick();

        // The voltage keeps arriving; the current does not.
        for (int i = 1; i <= 30; i++) h.Sample("N.a.v", 400 + i, T0 + i);
        for (int tick = 0; tick < 5; tick++) await h.Tick();

        h.Published.Should().HaveCount(1,
            "a power whose current sensor has stopped is unknown, not the power it had when the "
            + "sensor was last heard from");
    }

    [Fact]
    public async Task AChannelCanReadAnotherComputedChannelPublishedInTheSamePass()
    {
        // Declaration order is the contract: the third expression sees what the first two
        // published a moment ago, at the same instant, so they agree rather than being a tick
        // apart.
        var h = new Harness(
            "p_in[W] = a.v * a.i",
            "p_out[W] = b.v * b.i",
            "eff[%] = 100 * p_out / p_in");

        foreach (double t in new[] { T0, T0 + 1 })
        {
            h.Sample("N.a.v", 400, t);
            h.Sample("N.a.i", 25, t);
            h.Sample("N.b.v", 48, t);
            h.Sample("N.b.i", 190, t);
        }

        await h.Tick();

        h.Published.Select(p => p.Variable).Should().Equal("p_in", "p_out", "eff");
        h.Published.Last().Value.Should().BeApproximately(100.0 * (48 * 190) / (400 * 25), 1e-9);
    }

    [Fact]
    public async Task AnExpressionWithNoValueIsWithheldAndCounted()
    {
        var h = new Harness("eff[%] = 100 * a.out / a.in");
        h.Sample("N.a.out", 900, T0);
        h.Sample("N.a.in", 0, T0);          // a zero denominator: the ratio has no value

        await h.Tick();

        h.Published.Should().BeEmpty();
        h.Pump.Withheld.Should().Be(1,
            "a quiet derived channel and one whose inputs cannot produce a value look identical "
            + "from outside, and only the second means somebody should look at the rig");
    }

    [Fact]
    public async Task AChannelThatThrowsIsSkippedAndTheOthersKeepRunning()
    {
        // The failure this exists to prevent: the pump published for a while, threw, and went
        // quiet, and every surface showed a derived channel that had simply stopped arriving.
        var server = new TelemetryStreamingServer(port: 0)
        {
            Computed = new[] { "bad = a.v * 2", "good = a.v * 3" }
                .Select(ComputedChannel.Parse).ToList()
        };

        var published = new List<string>();
        var pump = new ComputedChannelPump(server, (packet, _, _) =>
        {
            if (packet.Variable == "bad") throw new InvalidOperationException("boom");
            published.Add(packet.Variable);
            return ValueTask.CompletedTask;
        });

        server.Series.Append("N.a.v", 400, T0);
        await pump.TickAsync();

        server.Series.Append("N.a.v", 401, T0 + 1);
        await pump.TickAsync();

        published.Should().Equal("good", "good");
        pump.Faulted.Should().Be(1, "the broken channel is abandoned rather than retried every tick");
        pump.FaultMessage.Should().Contain("bad").And.Contain("boom");
    }

    /// <summary>A source that yields nothing and ends, like a recording that has run out.</summary>
    private sealed class ExhaustedSource : ITelemetrySource
    {
        public string Origin => "TEST";
        public bool IsSimulated => true;
        public string Description => "a source that is already finished";
        public string PortName => "COM-TEST";

        public async IAsyncEnumerable<RawPacket> ReadAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield break;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AnIngestRunOverAFinishedSourceReturnsInsteadOfWaitingForTheComputedLoop()
    {
        // The computed loop is a timer and never ends on its own. Started on the caller's token
        // and awaited when the read loop finished, it deadlocked every run over a finite source —
        // a replayed recording, or any test — and the whole suite hung on it rather than failing.
        var server = new TelemetryStreamingServer(port: 0);
        var pump = new TelemetryIngestPump(server, new ExhaustedSource());

        Task run = pump.RunAsync(CancellationToken.None);

        (await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10)))).Should().Be(run,
            "the read loop ending has to stop the computed loop with it");
        await run;
    }

    [Fact]
    public async Task TheCountersAreVisibleThroughTheServerWithoutKnowingThePumpsType()
    {
        var h = new Harness("p[W] = a.v * a.i");
        h.Sample("N.a.v", 400, T0);
        h.Sample("N.a.i", 25, T0);
        await h.Tick();

        h.Server.ComputedCounters.Should().NotBeNull(
            "/api/status reports these, and a pump nobody can see the counters of is what this replaces");
        h.Server.ComputedCounters!.Published.Should().Be(1);
    }
}
