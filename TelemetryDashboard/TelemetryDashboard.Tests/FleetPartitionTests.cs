using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using TelemetryDashboard.Core.Cluster;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Exercises fleet-scale coverage behaviour without a fleet.
/// </summary>
/// <remarks>
/// There is one machine here, so the multi-machine behaviour cannot be run for real. What can be
/// run is the part that decides whether an answer is complete, because that logic depends only on
/// who was heard from and when — and both are inputs. Driving them from a controlled clock
/// reproduces a partition exactly, and reproduces it deterministically, which a real network would
/// not.
///
/// This is a substitute for a fleet, not a claim to have one. It proves the ledger reasons
/// correctly about absence; it proves nothing about whether a thousand hosts can reach each other.
/// The distinction is the whole point of writing it down.
/// </remarks>
public class FleetPartitionTests
{
    private DateTimeOffset _now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private CoverageLedger Fleet(int size, TimeSpan? silence = null)
    {
        var ledger = new CoverageLedger(silence ?? TimeSpan.FromSeconds(30), () => _now);
        for (int i = 0; i < size; i++) ledger.RecordSample(NodeName(i));
        return ledger;
    }

    private static string NodeName(int index) => $"host-{index:0000}";

    private void ReportAll(CoverageLedger ledger, int size, IEnumerable<int>? except = null)
    {
        var silent = new HashSet<int>(except ?? Enumerable.Empty<int>());
        for (int i = 0; i < size; i++)
        {
            if (!silent.Contains(i)) ledger.RecordSample(NodeName(i));
        }
    }

    [Fact]
    public void ThousandNodesWithTwoSilentAreNotReportedAsHealthy()
    {
        const int fleet = 1_000;
        CoverageLedger ledger = Fleet(fleet);

        _now = _now.AddMinutes(1);
        ReportAll(ledger, fleet, except: new[] { 412, 977 });

        CoverageSnapshot snapshot = ledger.Snapshot();

        snapshot.IsComplete.Should().BeFalse();
        snapshot.Missing.Select(n => n.NodeId).Should().BeEquivalentTo("host-0412", "host-0977");

        // 998 of 1000 is 99.8%, which reads as healthy on any dashboard that shows a percentage.
        // The two nodes that stopped are the only ones worth looking at, and a number cannot say so.
        snapshot.ExpectedFraction.Should().BeApproximately(0.998, 1e-6);
        snapshot.Describe().Should().Contain("host-0412").And.Contain("Incomplete");
    }

    [Fact]
    public void APartitionThatHealsIsReportedComplete_AndTheHistoryIsNotRewritten()
    {
        const int fleet = 50;
        CoverageLedger ledger = Fleet(fleet);

        _now = _now.AddMinutes(2);
        ReportAll(ledger, fleet, except: Enumerable.Range(0, 10));
        ledger.Snapshot().Missing.Should().HaveCount(10, "the partition is open");

        _now = _now.AddSeconds(5);
        ReportAll(ledger, fleet);

        CoverageSnapshot healed = ledger.Snapshot();
        healed.IsComplete.Should().BeTrue("everyone is reporting again");

        // The counts still show the gap: the nodes that were cut off contributed fewer samples.
        // Coverage becoming complete must not erase that they were once absent.
        long reconnected = healed.Nodes.Single(n => n.NodeId == "host-0000").Samples;
        long neverLost = healed.Nodes.Single(n => n.NodeId == "host-0049").Samples;
        reconnected.Should().BeLessThan(neverLost);
    }

    [Fact]
    public void HalfTheFleetGoingDarkIsNotAveragedAway()
    {
        const int fleet = 100;
        CoverageLedger ledger = Fleet(fleet);

        _now = _now.AddMinutes(1);
        ReportAll(ledger, fleet, except: Enumerable.Range(0, 50));

        CoverageSnapshot snapshot = ledger.Snapshot();

        snapshot.Reporting.Should().HaveCount(50);
        snapshot.Missing.Should().HaveCount(50);

        // Five names and a count, not a hundred lines. A report nobody can read is not a report.
        string described = snapshot.Describe();
        described.Should().Contain("45 more");
        described.Should().Contain("50 of 100");
    }

    [Fact]
    public void ANodeAddedMidRunIsExpectedFromThenOn()
    {
        CoverageLedger ledger = Fleet(3);

        _now = _now.AddSeconds(5);
        ledger.RecordSample("host-new");
        ledger.Snapshot().IsComplete.Should().BeTrue();

        _now = _now.AddMinutes(5);
        ReportAll(ledger, 3);

        // Nobody declared host-new, and its disappearance still has to be visible. A fleet that
        // grows by discovery is the normal case; a fleet that only knows its configured members
        // cannot notice anything it was not told about in advance.
        ledger.Snapshot().Missing.Should().ContainSingle().Which.NodeId.Should().Be("host-new");
    }

    [Fact]
    public void TheLedgerCostsLittlePerNode()
    {
        // Not a scale claim — it is one process holding a dictionary. It bounds the memory the
        // coverage answer costs, so nobody has to guess whether tracking a fleet is affordable.
        const int fleet = 100_000;

        long before = GC.GetAllocatedBytesForCurrentThread();
        CoverageLedger ledger = Fleet(fleet);
        long perNode = (GC.GetAllocatedBytesForCurrentThread() - before) / fleet;

        ledger.Snapshot().Nodes.Should().HaveCount(fleet);
        perNode.Should().BeLessThan(400,
            "an entry is a name, a timestamp and two counters; anything much larger is a leak");
    }
}
