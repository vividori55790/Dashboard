using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using TelemetryDashboard.Core.Cluster;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Covers the two properties that keep a many-machine answer honest: a node cannot go missing
/// quietly, and two machines cannot claim the same channel.
/// </summary>
public class ClusterCoverageTests
{
    private DateTimeOffset _now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private CoverageLedger Ledger(TimeSpan? threshold = null) =>
        new(threshold ?? TimeSpan.FromSeconds(30), () => _now);

    [Fact]
    public void ANodeThatStopsReportingIsNamedAsMissing_NotQuietlyExcluded()
    {
        CoverageLedger ledger = Ledger();
        ledger.RecordSample("alpha");
        ledger.RecordSample("bravo");

        _now = _now.AddMinutes(5);
        ledger.RecordSample("alpha");

        CoverageSnapshot snapshot = ledger.Snapshot();

        snapshot.IsComplete.Should().BeFalse();
        snapshot.Missing.Should().ContainSingle().Which.NodeId.Should().Be("bravo");
        snapshot.Describe().Should().Contain("bravo").And.Contain("Incomplete");
    }

    [Fact]
    public void TheDescriptionSaysTheFiguresExcludeTheMissingNodes()
    {
        CoverageLedger ledger = Ledger();
        ledger.RecordSample("alpha");
        ledger.Expect("bravo");

        // An operator who skims "2 of 3" reads it as nearly everything. Saying the numbers exclude
        // the absent nodes is the part that turns it into something to act on.
        ledger.Snapshot().Describe().Should().Contain("exclude");
    }

    [Fact]
    public void ANodeThatNeverStartedIsReportedOnlyIfItWasDeclared()
    {
        CoverageLedger learned = Ledger();
        learned.RecordSample("alpha");
        learned.Snapshot().IsComplete.Should().BeTrue("nothing else was ever expected");

        CoverageLedger declared = Ledger();
        declared.Expect("charlie");
        declared.RecordSample("alpha");

        CoverageSnapshot snapshot = declared.Snapshot();
        snapshot.IsComplete.Should().BeFalse();
        snapshot.Missing.Single().Presence.Should().Be(NodePresence.NeverSeen);
        snapshot.Describe().Should().Contain("never seen");
    }

    [Fact]
    public void AnythingHeardOnceIsExpectedThereafter()
    {
        CoverageLedger ledger = Ledger();
        ledger.RecordSample("delta");

        ledger.KnownNodes.Should().Contain("delta",
            "a node that worked and then stopped is the common failure, and nobody maintains a list");

        _now = _now.AddHours(2);
        ledger.Snapshot().Missing.Should().ContainSingle().Which.NodeId.Should().Be("delta");
    }

    [Fact]
    public void TheLearnedSetSurvivesARestartWhenItIsPersistedBack()
    {
        CoverageLedger before = Ledger();
        before.RecordSample("echo");
        before.RecordSample("foxtrot");

        // Without this the hub forgets a node ever existed at exactly the moment someone restarts
        // it to find out why data is missing.
        CoverageLedger after = Ledger();
        foreach (string node in before.KnownNodes) after.Expect(node);

        after.Snapshot().Missing.Select(n => n.NodeId).Should().BeEquivalentTo("echo", "foxtrot");
    }

    [Fact]
    public void ALateSampleDoesNotMakeALiveNodeLookSilent()
    {
        CoverageLedger ledger = Ledger();
        ledger.RecordSample("golf", _now);
        ledger.RecordSample("golf", _now.AddMinutes(-10));

        ledger.Snapshot().IsComplete.Should().BeTrue("out-of-order arrival is normal across a network");
    }

    [Fact]
    public void SilenceNeverRetiresANodeOnItsOwn()
    {
        CoverageLedger ledger = Ledger();
        ledger.RecordSample("hotel");
        _now = _now.AddDays(3);

        ledger.Snapshot().Missing.Should().ContainSingle(
            "a node going quiet is the event worth knowing about, not a reason to forget it");

        ledger.Retire("hotel").Should().BeTrue();
        ledger.Snapshot().Nodes.Should().BeEmpty();
    }

    [Fact]
    public void AZeroSilenceThresholdIsRefused()
    {
        Action build = () => _ = new CoverageLedger(TimeSpan.Zero);
        build.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AnIdentityIsStableAcrossRestartsAndIndependentOfTheMachineName()
    {
        string directory = Path.Combine(Path.GetTempPath(), "td-identity-" + Guid.NewGuid().ToString("N"));

        try
        {
            NodeIdentity first = NodeIdentity.LoadOrCreate(directory);
            NodeIdentity second = NodeIdentity.LoadOrCreate(directory);

            first.WasCreated.Should().BeTrue();
            second.WasCreated.Should().BeFalse();
            second.Id.Should().Be(first.Id, "history splits in two if the id changes between runs");
            first.Id.Should().NotContain(Environment.MachineName,
                "hostnames are cloned with machine images and renamed by administrators");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ACorruptIdentityFileIsReplacedRatherThanTrusted()
    {
        string directory = Path.Combine(Path.GetTempPath(), "td-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, NodeIdentity.FileName), "not a valid id!!");

        try
        {
            NodeIdentity identity = NodeIdentity.LoadOrCreate(directory);

            identity.WasCreated.Should().BeTrue();
            identity.Id.Should().MatchRegex("^[A-Za-z0-9_-]{4,64}$",
                "a malformed id would be stamped onto every record this node ever emits");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AnAssignedIdIsRejectedRatherThanSanitisedIntoAPossibleCollision()
    {
        Action bad = () => NodeIdentity.FromAssignedId("line 3/rack A");
        bad.Should().Throw<ArgumentException>().WithMessage("*collide*");

        NodeIdentity.FromAssignedId("line3-rackA").Id.Should().Be("line3-rackA");
    }

    [Fact]
    public void QualifyingAChannelKeepsTwoMachinesApart()
    {
        NodeIdentity first = NodeIdentity.FromAssignedId("host-0001");
        NodeIdentity second = NodeIdentity.FromAssignedId("host-0002");

        first.Qualify("MCU_NODE_1.TEMP").Should().NotBe(second.Qualify("MCU_NODE_1.TEMP"));
    }
}
