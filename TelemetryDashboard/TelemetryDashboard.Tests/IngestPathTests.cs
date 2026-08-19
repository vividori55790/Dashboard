using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Records;
using TelemetryDashboard.Core.Streaming;
using TelemetryDashboard.Host.Ingest;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Covers the headless host's ingest path: what arrives, what is published, and what is counted.
/// </summary>
/// <remarks>
/// The path had no tests at all while it was the only route data takes through the headless
/// product. These assert the two properties that matter and that nothing checked before: nothing
/// that arrives disappears without a counter, and a synthetic sample cannot lose its mark.
/// </remarks>
public class IngestPathTests
{
    /// <summary>A source that replays a fixed script and then ends, so a run is deterministic.</summary>
    private sealed class ScriptedSource : ITelemetrySource
    {
        private readonly IReadOnlyList<string> _lines;

        public ScriptedSource(bool simulated, params string[] lines)
        {
            IsSimulated = simulated;
            _lines = lines;
        }

        public string Origin => IsSimulated ? "SIMULATED" : "REAL_HARDWARE";
        public bool IsSimulated { get; }
        public string Description => "scripted test source";
        public string PortName { get; init; } = "COM-TEST";

        public async IAsyncEnumerable<RawPacket> ReadAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (string line in _lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new RawPacket(PortName, line, DateTime.UtcNow);
                await Task.Yield();
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static TelemetryStreamingServer UnstartedServer() => new(18099);

    [Fact]
    public async Task LinesNothingCanParseAreCountedWithAnExample_NotSilentlyDropped()
    {
        await using TelemetryStreamingServer server = UnstartedServer();
        var source = new ScriptedSource(
            simulated: false,
            "WATCHDOG RESET",
            "WATCHDOG RESET",
            "BROWNOUT DETECTED",
            "{\"nodeId\":\"MCU_A\",\"temp\":41.9}");

        var pump = new TelemetryIngestPump(server, source);
        await pump.RunAsync(CancellationToken.None);

        pump.Records.Unrecognised.Total.Should().Be(3);
        pump.SamplesPublished.Should().Be(1, "the one readable line still gets through");

        IReadOnlyList<UnrecognisedShape> shapes = pump.Records.Unrecognised.Shapes();
        shapes.Should().HaveCount(2);
        shapes[0].Prefix.Should().Be("WATCHDOG");
        shapes[0].Count.Should().Be(2);
        shapes[0].Example.Should().Be("WATCHDOG RESET", "an operator needs the line, not just a tally");
    }

    [Fact]
    public async Task EveryArrivalIsAttributedToAStage()
    {
        await using TelemetryStreamingServer server = UnstartedServer();
        var source = new ScriptedSource(
            simulated: false,
            "{\"nodeId\":\"MCU_A\",\"temp\":41.9}",
            "WATCHDOG RESET");

        var pump = new TelemetryIngestPump(server, source);
        await pump.RunAsync(CancellationToken.None);

        IReadOnlyList<StageActivity> activity = pump.Records.Activity();
        StageActivity telemetry = activity.Single(a => a.Stage == "telemetry");
        StageActivity unreadable = activity.Single(a => a.Stage == "unrecognised-lines");

        telemetry.Accepted.Should().Be(1);
        telemetry.Declined.Should().Be(1, "the text record is refused by the numeric stage, not lost");
        unreadable.Accepted.Should().Be(1);
        unreadable.Declined.Should().Be(1);
        telemetry.Faulted.Should().Be(0);
        unreadable.Faulted.Should().Be(0);
    }

    [Fact]
    public async Task SyntheticSamplesKeepTheirMarkAcrossTheProjection()
    {
        await using TelemetryStreamingServer server = UnstartedServer();
        var source = new ScriptedSource(simulated: true, "{\"nodeId\":\"MCU_A\",\"temp\":41.9}");

        var pump = new TelemetryIngestPump(server, source);
        await pump.RunAsync(CancellationToken.None);

        pump.SamplesPublished.Should().Be(1);
        server.DvrPlayer.FrameCount.Should().Be(1);

        // The DVR timeline is where a synthetic sample would become indistinguishable from a
        // measurement if the record round trip dropped the flag.
        server.DvrPlayer.GetFramesInRange(0, double.MaxValue)
            .Single().ChannelName.Should().StartWith("SIM:");
    }

    [Fact]
    public async Task APacketThatAlreadyClaimsToBeSimulatedIsRefusedRatherThanLaundered()
    {
        var path = new IngestRecordPath((_, _, _) => ValueTask.CompletedTask, isSimulated: false);
        var packet = new TelemetryPacket("MCU_A", "temp", 41.9, "C") { Flags = PacketFlags.Simulated };

        Func<Task> offer = async () => await path.OfferPacketAsync(packet, "COM-TEST");

        await offer.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*pre-marked as simulated*");
    }

    [Fact]
    public async Task ThePortTheSampleArrivedOnSurvivesTheProjection()
    {
        var ports = new List<string>();
        var path = new IngestRecordPath(
            (_, port, _) => { ports.Add(port); return ValueTask.CompletedTask; },
            isSimulated: false);

        await path.OfferPacketAsync(new TelemetryPacket("MCU_A", "temp", 41.9, "C"), "/dev/ttyUSB0");

        ports.Should().ContainSingle().Which.Should().Be("/dev/ttyUSB0");
    }

    [Fact]
    public void ARunawayChannelIsIsolatedAndTheLossIsCounted()
    {
        var guard = new IngestRateGuard(maxChannelRatePerSecond: 10);

        int allowed = Enumerable.Range(0, 50).Count(_ => guard.Allow("MCU_A.temp"));

        allowed.Should().BeLessThan(50, "the guard must actually refuse something");
        guard.DroppedSamples.Should().Be(50 - allowed);
        guard.Isolations.Should().BeGreaterThan(0);
        guard.Summary().Should().Contain("dropped");
    }

    [Fact]
    public void AChannelUnderTheLimitIsNeverTouched()
    {
        var guard = new IngestRateGuard(maxChannelRatePerSecond: 5_000);

        Enumerable.Range(0, 200).All(_ => guard.Allow("MCU_A.temp")).Should().BeTrue();
        guard.DroppedSamples.Should().Be(0);
        guard.Summary().Should().BeNull("silence is only correct when nothing was lost");
    }

    [Fact]
    public void TheGuardCanBeTurnedOffEntirely()
    {
        var guard = new IngestRateGuard(maxChannelRatePerSecond: 0);

        guard.IsActive.Should().BeFalse();
        Enumerable.Range(0, 10_000).All(_ => guard.Allow("MCU_A.temp")).Should().BeTrue();
        guard.DroppedSamples.Should().Be(0);
    }

    [Theory]
    [InlineData("$HIST,1,2", "$HIST")]
    [InlineData("WATCHDOG RESET", "WATCHDOG")]
    [InlineData("key=value", "key")]
    [InlineData("   ", "(blank)")]
    [InlineData("NOSEPARATORSATALLHEREOK", "NOSEPARATORSATAL\u2026")]
    public void AnUnparsedLineIsIdentifiedByItsLeadingToken(string line, string expected) =>
        UnrecognisedLineStage.ShapeOf(line).Should().Be(expected);
}
