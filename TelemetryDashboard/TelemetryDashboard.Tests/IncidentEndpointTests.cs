using System;
using System.Collections.Generic;
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
/// <c>/api/incident</c>: an alert's timestamp turned into the run-up to it.
/// </summary>
/// <remarks>
/// <c>FailureSnapshotExtractor</c> lived in <c>Infrastructure/Storage</c> and depends on nothing but
/// <c>TelemetryPacket</c>, so Core — which must not reference Infrastructure — could not reach it
/// from the endpoint layer where an incident window is actually asked for.
/// <para>
/// The window is asymmetric on purpose. What happened <em>before</em> a fault is what explains it,
/// so the lead is long and the tail is short, and the tail exists only to show how the system
/// responded.
/// </para>
/// </remarks>
public class IncidentEndpointTests
{
    private sealed class FakeStore : IDataLogger
    {
        private readonly List<TelemetryPacket> _packets = new();

        public void Seed(TelemetryPacket packet) => _packets.Add(packet);

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

        public Task<IEnumerable<TelemetryPacket>> QueryAsync(QueryFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(_packets
                .Where(p => filter.NodeId is null || p.NodeId == filter.NodeId)
                .Where(p => filter.StartTime is null || p.Timestamp >= filter.StartTime)
                .Where(p => filter.EndTime is null || p.Timestamp <= filter.EndTime)
                .Take(filter.Limit)
                .AsEnumerable());
    }

    private static readonly DateTime Instant = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>One sample a second for two minutes either side of the instant, on two channels.</summary>
    private static FakeStore TwoMinutesEitherSide()
    {
        var store = new FakeStore();
        for (int i = -120; i <= 120; i++)
        {
            store.Seed(new TelemetryPacket("COM3", "dab.bus_voltage", 400 + i * 0.1, "V", Instant.AddSeconds(i)));
            store.Seed(new TelemetryPacket("COM4", "psfb.output_voltage", 48 + i * 0.01, "V", Instant.AddSeconds(i)));
        }
        return store;
    }

    [Fact]
    public async Task TheWindowLooksMostlyBackwards()
    {
        IncidentEndpoint.Result result =
            await IncidentEndpoint.QueryAsync(TwoMinutesEitherSide(), Instant, 0, 0, null);

        result.Status.Should().Be("Success");
        result.LeadSec.Should().Be(IncidentEndpoint.DefaultLeadSec);
        result.TrailSec.Should().Be(IncidentEndpoint.DefaultTrailSec);

        IncidentEndpoint.ChannelWindow channel = result.Channels.First();
        DateTime[] stamps = channel.Timestamps.Select(DateTime.Parse).Select(d => d.ToUniversalTime()).ToArray();

        stamps.Count(t => t < Instant).Should().Be(10, "ten seconds of lead-up, one sample a second");
        stamps.Count(t => t > Instant).Should().Be(2, "and a short tail showing the response");
    }

    [Fact]
    public async Task EveryChannelThatReportedInTheWindowIsIncluded()
    {
        IncidentEndpoint.Result result =
            await IncidentEndpoint.QueryAsync(TwoMinutesEitherSide(), Instant, 0, 0, null);

        result.ChannelCount.Should().Be(2);
        result.Channels.Select(c => c.Variable).Should()
            .BeEquivalentTo("dab.bus_voltage", "psfb.output_voltage");
        result.Channels.Should().OnlyContain(c => c.Samples == 13, "ten before, one at, two after");
    }

    [Fact]
    public async Task TheLastValueBeforeTheInstantIsReportedSeparately()
    {
        // The state that led to the fault. A reading taken after it does not describe that state,
        // and offering one as though it did is the mistake the whole window exists to avoid.
        IncidentEndpoint.Result result =
            await IncidentEndpoint.QueryAsync(TwoMinutesEitherSide(), Instant, 0, 0, null);

        IncidentEndpoint.ChannelWindow dab =
            result.Channels.Single(c => c.Variable == "dab.bus_voltage");

        dab.ValueBefore.Should().Be(400.0, "the sample at the instant itself is the last one at or before it");
        dab.Minimum.Should().BeLessThan(dab.ValueBefore!.Value);
        dab.Maximum.Should().BeGreaterThan(dab.ValueBefore.Value);
    }

    [Fact]
    public async Task AChannelSilentBeforeTheInstantReportsNoValueBefore()
    {
        var store = new FakeStore();
        // Only reports after the instant: the channel came back to life as the response, and there
        // is no earlier state to show.
        store.Seed(new TelemetryPacket("COM3", "late", 5.0, "V", Instant.AddSeconds(1)));

        IncidentEndpoint.Result result =
            await IncidentEndpoint.QueryAsync(store, Instant, 0, 0, null);

        result.Channels.Single().ValueBefore.Should().BeNull(
            "there is no reading from before the instant, and the first one after it is not a substitute");
    }

    [Fact]
    public async Task AnInstantWithNothingAroundItIsAnEmptyWindowRatherThanTheNearestRows()
    {
        var store = new FakeStore();
        store.Seed(new TelemetryPacket("COM3", "temp", 40.0, "C", Instant.AddHours(-3)));

        IncidentEndpoint.Result result =
            await IncidentEndpoint.QueryAsync(store, Instant, 0, 0, null);

        result.Status.Should().Be("Success");
        result.ChannelCount.Should().Be(0);
        result.TotalSamples.Should().Be(0);
    }

    [Fact]
    public async Task NamingNoInstantIsRefusedRatherThanGuessed()
    {
        // The archive stores measurements and not verdicts, so this endpoint cannot claim to have
        // found an incident. It answers about a moment somebody else identified.
        IncidentEndpoint.Result result =
            await IncidentEndpoint.QueryAsync(new FakeStore(), null, 0, 0, null);

        result.Status.Should().Be("Error");
        result.Reason.Should().Contain("at=");
    }

    [Fact]
    public async Task AHostWithNoArchiveSaysSo()
    {
        IncidentEndpoint.Result result = await IncidentEndpoint.QueryAsync(null, Instant, 0, 0, null);

        result.Status.Should().Be("Error");
        result.Reason.Should().Contain("--archive");
    }

    [Fact]
    public async Task AnAbsurdlyWideLeadIsCappedRatherThanAssembled()
    {
        IncidentEndpoint.Result result = await IncidentEndpoint.QueryAsync(
            TwoMinutesEitherSide(), Instant, leadSec: 999_999, trailSec: 2, null);

        result.LeadSec.Should().Be(IncidentEndpoint.MaximumLeadSec);
    }

    [Fact]
    public async Task OneNodeCanBeAskedAboutOnItsOwn()
    {
        IncidentEndpoint.Result result =
            await IncidentEndpoint.QueryAsync(TwoMinutesEitherSide(), Instant, 0, 0, node: "COM4");

        result.ChannelCount.Should().Be(1);
        result.Channels.Single().NodeId.Should().Be("COM4");
    }
}
