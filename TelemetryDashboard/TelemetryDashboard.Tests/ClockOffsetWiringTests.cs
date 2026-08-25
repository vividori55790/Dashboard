using System.Net.Http.Json;
using System.Text.Json;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The offset estimator, reachable from a running program for the first time.
/// </summary>
/// <remarks>
/// It has existed since M1 and never run: <c>SyncNodeClock</c> had no caller outside a test, so
/// every offset it could have reported was zero. It could not have run any earlier either — a
/// remote sample was stamped with the receiver's clock at ingest and the sender's timestamp was
/// discarded, and an offset between two clocks cannot be estimated when only one survives.
/// <para>
/// Checked here against a known answer rather than a plausible one. Both ends of the live pair this
/// was measured on read the same machine clock, so the true offset is exactly zero; the estimate
/// came back +0.57 ms with a spread of 6.8 ms over 64 observations — above the truth, as
/// <c>offset + transit</c> with a non-negative transit requires, and bounded by an error bar that
/// covers it.
/// </para>
/// </remarks>
public class ClockOffsetWiringTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void AHostNothingReachesOverANetworkReportsNoClocksRatherThanZeroOffsets()
    {
        // The distinction the whole project is organised around, at this scale: a ledger that is
        // attached and has heard nothing is not a fleet whose clocks all agree.
        var server = new TelemetryStreamingServer(port: 0)
        {
            Clocks = new Core.Services.TimeSyncJitterBuffer().ObservedClocks
        };

        server.Clocks!().Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ANodeReportingWithoutAClockOfItsOwnIsAbsentRatherThanUnmeasured()
    {
        // A device on this machine's own port has one clock, and it is already this host's. Listing
        // it with an unmeasured offset would fill the fleet view with rows that say nothing.
        var buffer = new Core.Services.TimeSyncJitterBuffer();
        buffer.EnqueueSample("local-rig", 100.0, 400.0);

        buffer.ObservedClocks().Should().BeEmpty();
        buffer.GetClockOffset("local-rig").HasOffset.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheOffsetLandsAboveTheTruthAndInsideItsOwnErrorBar()
    {
        // The property that makes the estimate trustworthy, stated as an invariant rather than as
        // the one number a live run happened to produce. Transit cannot be negative, so every
        // observation overstates; the minimum is the least overstated and must still be >= truth.
        var buffer = new Core.Services.TimeSyncJitterBuffer();
        const double truth = 4.0;
        double[] transits = [0.031, 0.004, 0.120, 0.017, 0.009];

        double nodeTime = 1000.0;
        foreach (double transit in transits)
        {
            buffer.SyncNodeClock("PEER-01", nodeTime + truth + transit, nodeTime);
            nodeTime += 1.0;
        }

        ClockOffsetEstimate estimate = buffer.GetClockOffset("PEER-01");

        estimate.OffsetSec.Should().BeGreaterThanOrEqualTo(truth,
            "an offset below the truth would mean a message arrived before it was sent");
        (estimate.OffsetSec - truth).Should().BeLessThanOrEqualTo(estimate.SpreadSec!.Value,
            "the error bar has to cover the truth, or it is decoration");
        estimate.Samples.Should().Be(transits.Length);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task TheStatusPayloadPublishesTheErrorBarBesideEveryOffset()
    {
        var buffer = new Core.Services.TimeSyncJitterBuffer();
        buffer.SyncNodeClock("PEER-01", 1004.00, 1000.0);
        buffer.SyncNodeClock("PEER-01", 1005.02, 1001.0);

        await using var server = new TelemetryStreamingServer(port: 18141) { Clocks = buffer.ObservedClocks };
        server.Start(string.Empty);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        JsonElement status = await client.GetFromJsonAsync<JsonElement>("http://localhost:18141/api/status");

        JsonElement clocks = status.GetProperty("clocks");
        clocks.GetProperty("nodes").GetInt32().Should().Be(1);

        JsonElement node = clocks.GetProperty("perNode")[0];
        node.GetProperty("node").GetString().Should().Be("PEER-01");
        node.GetProperty("spreadSec").ValueKind.Should().NotBe(JsonValueKind.Null,
            "an offset published without its spread is the point estimate §3 was written against");
        node.GetProperty("uncertaintyIsALowerBound").GetBoolean().Should().BeTrue(
            "one-way messages never separate transit from the offset, so a consumer reading "
            + "spreadSec as the whole uncertainty will order events it cannot order");
        node.GetProperty("summary").GetString().Should().Contain("lower bound");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task NoLedgerAttachedReadsDifferentlyFromALedgerThatHasHeardNothing()
    {
        await using var server = new TelemetryStreamingServer(port: 18142);
        server.Start(string.Empty);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        JsonElement status = await client.GetFromJsonAsync<JsonElement>("http://localhost:18142/api/status");

        status.GetProperty("clocks").ValueKind.Should().Be(JsonValueKind.Null,
            "null is 'nobody is comparing clocks'; an empty list is 'somebody is, and no sample has "
            + "carried one'. Only the second says anything about the fleet");
    }
}
