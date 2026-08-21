using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Streaming;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// <c>/api/history</c>: the only endpoint that can be asked about a time the host was not running.
/// </summary>
/// <remarks>
/// The headless host is the cross-platform product and had no durable store at all — a CSV
/// transcript and a few minutes of in-memory ring. "What did this channel do last Tuesday" had no
/// answer anywhere on Linux or macOS.
/// </remarks>
public class HistoryEndpointTests
{
    /// <summary>An in-memory store, so these assertions are about the endpoint rather than SQLite.</summary>
    private sealed class FakeStore : IDataLogger
    {
        private readonly List<TelemetryPacket> _packets = new();

        public QueryFilter? LastFilter { get; private set; }

        public void Seed(params TelemetryPacket[] packets) => _packets.AddRange(packets);

        public Task WriteAsync(TelemetryPacket packet, CancellationToken cancellationToken = default)
        {
            _packets.Add(packet);
            return Task.CompletedTask;
        }

        public Task WriteBatchAsync(IEnumerable<TelemetryPacket> packets, CancellationToken cancellationToken = default)
        {
            _packets.AddRange(packets);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<TelemetryPacket>> QueryAsync(QueryFilter filter, CancellationToken cancellationToken = default)
        {
            LastFilter = filter;

            IEnumerable<TelemetryPacket> found = _packets
                .Where(p => filter.NodeId is null || p.NodeId == filter.NodeId)
                .Where(p => filter.Variable is null || p.Variable == filter.Variable)
                .Where(p => filter.StartTime is null || p.Timestamp >= filter.StartTime)
                .Where(p => filter.EndTime is null || p.Timestamp <= filter.EndTime)
                .Take(filter.Limit);

            return Task.FromResult(found);
        }
    }

    private static TelemetryPacket Sample(string node, string variable, double value, DateTime at) =>
        new(node, variable, value, "V", at);

    [Fact]
    public async Task AHostWithNoArchiveSaysSoRatherThanReturningNothing()
    {
        // An empty result and "there is no archive" are different facts, and only one of them means
        // the operator should restart with --archive.
        HistoryEndpoint.Result result =
            await HistoryEndpoint.QueryAsync(null, null, null, null, null, 10);

        result.Status.Should().Be("Error");
        result.Reason.Should().Contain("--archive");
        result.Samples.Should().BeEmpty();
    }

    [Fact]
    public async Task SamplesComeBackWithTheirNodeUnitAndTimestamp()
    {
        var now = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var store = new FakeStore();
        store.Seed(
            Sample("SIM:COM3", "dab.bus_voltage", 400.5, now.AddSeconds(-3)),
            Sample("SIM:COM3", "dab.bus_voltage", 401.5, now.AddSeconds(-2)));

        HistoryEndpoint.Result result = await HistoryEndpoint.QueryAsync(
            store, null, "dab.bus_voltage", now.AddMinutes(-5), now, 10);

        result.Status.Should().Be("Success");
        result.Count.Should().Be(2);
        result.Samples.Select(s => s.Value).Should().BeEquivalentTo(new[] { 400.5, 401.5 });
        result.Samples.Should().OnlyContain(s => s.NodeId == "SIM:COM3" && s.Unit == "V");
        result.Samples[0].TimestampIso.Should().Contain("2026-03-01");
    }

    [Fact]
    public async Task AFullPageIsReportedAsTruncatedRatherThanLookingComplete()
    {
        // A truncated answer and a complete one look identical from the outside, and a reader who
        // cannot tell them apart concludes the machine went quiet at whatever moment the cap fell.
        var now = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var store = new FakeStore();
        for (int i = 0; i < 20; i++) store.Seed(Sample("N", "v", i, now.AddSeconds(-i)));

        HistoryEndpoint.Result full = await HistoryEndpoint.QueryAsync(
            store, null, "v", now.AddMinutes(-5), now, limit: 5);

        full.Count.Should().Be(5);
        full.Truncated.Should().BeTrue();

        HistoryEndpoint.Result complete = await HistoryEndpoint.QueryAsync(
            store, null, "v", now.AddMinutes(-5), now, limit: 100);

        complete.Count.Should().Be(20);
        complete.Truncated.Should().BeFalse("everything that matched was returned");
    }

    [Fact]
    public async Task TheRequestedLimitIsAskedForPlusOneSoTruncationCanBeDetected()
    {
        var store = new FakeStore();
        var now = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        await HistoryEndpoint.QueryAsync(store, null, "v", now.AddMinutes(-1), now, limit: 7);

        store.LastFilter!.Limit.Should().Be(8,
            "asking for exactly the limit makes a full page and a complete answer indistinguishable");
    }

    [Fact]
    public async Task ANoLimitRequestIsCappedRatherThanUnbounded()
    {
        var store = new FakeStore();
        var now = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        HistoryEndpoint.Result result =
            await HistoryEndpoint.QueryAsync(store, null, null, now.AddMinutes(-1), now, limit: 0);

        result.Limit.Should().Be(HistoryEndpoint.MaximumLimit,
            "an unbounded query over a month of archive would materialise the lot into memory");
    }

    [Fact]
    public async Task AWindowThatEndsBeforeItStartsIsRefused()
    {
        var store = new FakeStore();
        var now = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        HistoryEndpoint.Result result =
            await HistoryEndpoint.QueryAsync(store, null, null, now, now.AddMinutes(-5), 10);

        result.Status.Should().Be("Error");
        result.Reason.Should().Contain("ends before it starts");
    }

    [Theory]
    [InlineData("2026-03-01T12:00:00Z", 12)]
    [InlineData("2026-03-01T21:00:00+09:00", 12)]   // Seoul noon-plus-nine is 12:00 UTC
    [InlineData("2026-03-01T12:00:00", 12)]          // no offset: read as UTC, not as server-local
    public void ATimestampIsAlwaysReadAsUtc(string raw, int expectedUtcHour)
    {
        // This combination was RoundtripKind | AdjustToUniversal, which .NET rejects outright --
        // and rejects before looking at the input, so it threw even when no timestamp was given.
        // Every request to the endpoint failed, and the failure hung rather than answering.
        DateTime? parsed = HistoryEndpoint.ReadTimestamp(raw);

        parsed.Should().NotBeNull();
        parsed!.Value.Hour.Should().Be(expectedUtcHour);
        parsed.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void AnAbsentOrUnreadableTimestampIsNullRatherThanAThrow()
    {
        HistoryEndpoint.ReadTimestamp(null).Should().BeNull();
        HistoryEndpoint.ReadTimestamp("").Should().BeNull();
        HistoryEndpoint.ReadTimestamp("last tuesday").Should().BeNull();
    }
}
