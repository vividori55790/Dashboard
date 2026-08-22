using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Host.Outbound;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Keeping the incident report that nobody was awake to ask for.
/// </summary>
/// <remarks>
/// <c>/api/incident</c> has answered with a verdict per channel for a while, and only to somebody
/// who asked at the right moment with the right timestamp. An alarm at three in the morning names
/// that moment and nobody types it; by the time anyone looks, the instant is a guess.
/// <para>
/// Measured on a live host replaying a channel that crosses a declared limit, with nothing at all
/// querying the endpoint: one report appeared naming <c>RIG.spiking</c> at 298.8 sigma, its window
/// spanning 47.96..54.04 V, beside a quiet channel correctly left alone and a two-sample channel
/// correctly left unjudged.
/// </para>
/// </remarks>
public class IncidentCaptureTests : IDisposable
{
    private sealed class Store : IDataLogger
    {
        private readonly List<TelemetryPacket> _packets = new();

        public void Seed(TelemetryPacket p) => _packets.Add(p);
        public Task WriteAsync(TelemetryPacket p, CancellationToken c = default) { _packets.Add(p); return Task.CompletedTask; }
        public Task WriteBatchAsync(IEnumerable<TelemetryPacket> p, CancellationToken c = default) { _packets.AddRange(p); return Task.CompletedTask; }

        public Task<IEnumerable<TelemetryPacket>> QueryAsync(QueryFilter filter, CancellationToken c = default) =>
            Task.FromResult(_packets
                .Where(p => filter.StartTime is null || p.Timestamp >= filter.StartTime)
                .Where(p => filter.EndTime is null || p.Timestamp <= filter.EndTime)
                .Take(filter.Limit)
                .AsEnumerable());
    }

    private static readonly DateTime Crossed = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan NoWait = TimeSpan.Zero;

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "incident-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static Store RigWithASpike()
    {
        var store = new Store();
        for (int i = 0; i < 200; i++)
        {
            DateTime at = Crossed.AddSeconds(-100 + i * 0.5);
            double wobble = 0.02 * Math.Sin(i * 2.399963);
            store.Seed(new TelemetryPacket("RIG", "spiking", 48.0 + wobble + (i is >= 150 and < 160 ? 6.0 : 0.0), "V", at));
            store.Seed(new TelemetryPacket("RIG", "quiet", 48.0 + 0.02 * Math.Sin(i * 5.700141), "V", at));
        }
        return store;
    }

    private static ScoredSample CrossingSample(ChannelLimit rule) => new(
        "RIG.spiking", "RIG", "spiking", 54.0, "V", Crossed, 298.8, true, "test", false,
        new[] { new BreachedLimit(rule, LimitTransition.Entered) });

    private static ChannelLimit Rule => ChannelLimit.Parse("spiking[V] < 50");

    // ---- what gets written ----------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task ACrossingWritesTheWindowThatLedToItWithoutAnybodyAsking()
    {
        var relay = new IncidentCaptureRelay(RigWithASpike(), _dir, captureDelay: NoWait);

        await relay.CaptureAsync(CrossingSample(Rule), new BreachedLimit(Rule, LimitTransition.Entered));

        relay.Captured.Should().Be(1);
        string[] files = Directory.GetFiles(_dir, "*.json");
        files.Should().ContainSingle();
        Path.GetFileName(files[0]).Should().StartWith("incident-").And.Contain("RIG_spiking");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task TheReportNamesTheRuleThatTrippedAndTheChannelThatMoved()
    {
        var relay = new IncidentCaptureRelay(RigWithASpike(), _dir, captureDelay: NoWait);
        await relay.CaptureAsync(CrossingSample(Rule), new BreachedLimit(Rule, LimitTransition.Entered));

        using var document = System.Text.Json.JsonDocument.Parse(
            await File.ReadAllTextAsync(Directory.GetFiles(_dir, "*.json")[0]));
        System.Text.Json.JsonElement root = document.RootElement;

        root.GetProperty("trigger").GetString().Should().Be("spiking[V] < 50");
        root.GetProperty("channel").GetString().Should().Be("RIG.spiking");
        root.GetProperty("report").GetProperty("Anomalous").EnumerateArray()
            .Select(e => e.GetString()).Should().Contain("RIG.spiking");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task TheReportIsWrittenWithNoByteOrderMarkSoAStrictParserCanReadIt()
    {
        // Found twice the same way. A backtest CSV was refused by a Python csv.DictReader on its
        // first use, and this report by json.load -- which rejects a marked document outright,
        // making a machine-readable artefact unreadable by the machines it was written for.
        var relay = new IncidentCaptureRelay(RigWithASpike(), _dir, captureDelay: NoWait);
        await relay.CaptureAsync(CrossingSample(Rule), new BreachedLimit(Rule, LimitTransition.Entered));

        byte[] bytes = await File.ReadAllBytesAsync(Directory.GetFiles(_dir, "*.json")[0]);

        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
    }

    // ---- when it does not write ------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void OnlyTheCrossingIsCapturedAndNotEverySampleAfterIt()
    {
        // A converter held outside its band for an hour is one incident. A report per sample would
        // bury the one that explains it, and fill the disk doing so.
        var relay = new IncidentCaptureRelay(RigWithASpike(), _dir, captureDelay: NoWait);
        ChannelLimit rule = Rule;

        foreach (LimitTransition transition in new[] { LimitTransition.Sustained, LimitTransition.Cleared, LimitTransition.None })
        {
            relay.OnSampleScored(null, new ScoredSample(
                "RIG.spiking", "RIG", "spiking", 54.0, "V", Crossed, 298.8, true, "test", false,
                new[] { new BreachedLimit(rule, transition) }));
        }

        relay.Captured.Should().Be(0);
        Directory.Exists(_dir).Should().BeFalse("nothing was written, so nothing was created");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task ASecondCrossingInsideTheQuietPeriodIsCountedRatherThanWritten()
    {
        // A limit sitting on its threshold enters and clears several times a minute, and a report
        // for each would be a directory nobody can read rather than a record of an incident.
        var relay = new IncidentCaptureRelay(
            RigWithASpike(), _dir, cooldown: TimeSpan.FromMinutes(5), captureDelay: NoWait);
        ChannelLimit rule = Rule;

        relay.OnSampleScored(null, CrossingSample(rule));
        relay.OnSampleScored(null, CrossingSample(rule));
        relay.OnSampleScored(null, CrossingSample(rule));

        await WaitForFilesAsync(1);
        relay.Throttled.Should().Be(2);
        Directory.GetFiles(_dir, "*.json").Should().ContainSingle();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task TwoRulesOnOneChannelAreTwoIncidents()
    {
        // Sharing a cooldown per channel would let whichever rule fired first hide the second, and
        // two limits on one channel describe two different faults.
        var relay = new IncidentCaptureRelay(
            RigWithASpike(), _dir, cooldown: TimeSpan.FromMinutes(5), captureDelay: NoWait);
        ChannelLimit ceiling = ChannelLimit.Parse("spiking[V] < 50");
        ChannelLimit floor = ChannelLimit.Parse("spiking[V] > 10");

        await relay.CaptureAsync(CrossingSample(ceiling), new BreachedLimit(ceiling, LimitTransition.Entered));
        await relay.CaptureAsync(CrossingSample(floor), new BreachedLimit(floor, LimitTransition.Entered));

        relay.Captured.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task AReportThatCannotBeWrittenIsCountedRatherThanThrown()
    {
        // A full or unwritable disk during an incident is a bad moment to take the host down, and
        // the alarm itself has already gone out through whichever relay carries it.
        string path = Path.Combine(_dir, "not-a-directory");
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(path, "this is a file, not a directory");

        var relay = new IncidentCaptureRelay(RigWithASpike(), path, captureDelay: NoWait);

        await relay.CaptureAsync(CrossingSample(Rule), new BreachedLimit(Rule, LimitTransition.Entered));

        relay.Failed.Should().Be(1);
        relay.Captured.Should().Be(0);
    }

    // ---- the encoding, once, for everything that writes a file ------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheSharedEncodingEmitsNoMarkAndTheReaderTakesOneOff()
    {
        Utf8Files.WithoutBom.GetPreamble().Should().BeEmpty();

        // Recordings already on disk carry the mark, so a fix that only changed the writer would
        // leave every file written before it unreadable.
        Utf8Files.StripMark("﻿Timestamp_ISO,Value").Should().Be("Timestamp_ISO,Value");
        Utf8Files.StripMark("Timestamp_ISO,Value").Should().Be("Timestamp_ISO,Value");
        Utf8Files.StripMark(string.Empty).Should().BeEmpty();
    }

    private async Task WaitForFilesAsync(int count)
    {
        for (int i = 0; i < 100 && (!Directory.Exists(_dir) || Directory.GetFiles(_dir, "*.json").Length < count); i++)
        {
            await Task.Delay(20);
        }
    }
}
