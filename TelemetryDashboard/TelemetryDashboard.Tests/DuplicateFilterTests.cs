using TelemetryDashboard.Core.Cluster;

namespace TelemetryDashboard.Tests;

/// <summary>
/// ARCHITECTURE §4's idempotent exchange: a reconnect that replays must not double-count.
/// </summary>
/// <remarks>
/// Observed rather than anticipated. While driving the backfill work, the test peer's connection
/// ended, <c>SseTelemetrySource</c> reconnected — which is what it is built to do — and the peer
/// replayed its buffer. The receiving host took the same four-hour-old sample twice and reported it
/// twice, with nothing anywhere able to notice.
/// <para>
/// Driven end to end afterwards against a peer whose link drops and replays sequences 1..30 under
/// one epoch on every connection. Over six connections the receiver reported <c>admitted: 30,
/// duplicatesRefused: 150</c>, and its series store held exactly the thirty distinct samples while
/// <c>/api/inputs</c> still reported all 300 as having arrived — which is the intended split: the
/// inventory answers "is this port sending me anything", and a replayed sample genuinely did arrive
/// on the wire.
/// </para>
/// </remarks>
public class DuplicateFilterTests
{
    private const string Node = "PEER-01";
    private const string Epoch = "6d133d40643e";

    [Fact]
    [Trait("Category", "Tier1")]
    public void AReplayedBufferIsTakenOnceAndRefusedAfterwards()
    {
        var filter = new DuplicateFilter();

        for (int seq = 1; seq <= 30; seq++) filter.Admit(Node, Epoch, seq).Should().BeTrue();
        for (int replay = 0; replay < 5; replay++)
        {
            for (int seq = 1; seq <= 30; seq++) filter.Admit(Node, Epoch, seq).Should().BeFalse();
        }

        filter.Admitted.Should().Be(30);
        filter.Duplicates.Should().Be(150, "five replays of thirty samples is what a flaky link does");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ASenderThatRestartsIsNotMistakenForAReplay()
    {
        // The failure the epoch exists to prevent, and it is worse than the one being prevented: a
        // counter restarting at one looks like a replay of everything, so a healthy peer's entire
        // stream would be silently discarded.
        var filter = new DuplicateFilter();

        for (int seq = 1; seq <= 10; seq++) filter.Admit(Node, Epoch, seq).Should().BeTrue();
        for (int seq = 1; seq <= 10; seq++) filter.Admit(Node, "afterrestart", seq).Should().BeTrue();

        filter.Admitted.Should().Be(20);
        filter.Duplicates.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TwoNodesOnOneLinkAreDeduplicatedSeparately()
    {
        // Per node, as §4 says. Sharing one counter would make node B's sample number seven look
        // like a replay of node A's.
        var filter = new DuplicateFilter();

        filter.Admit("NODE-A", Epoch, 7).Should().BeTrue();
        filter.Admit("NODE-B", Epoch, 7).Should().BeTrue();
        filter.Admit("NODE-A", Epoch, 7).Should().BeFalse();

        filter.Admitted.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ASampleWithNoSequenceIsAdmittedAndCountedAsUnwatched()
    {
        // The field that keeps the duplicate count readable. A link whose sender stamps nothing can
        // never report a duplicate, and zero there would otherwise read as a clean link rather than
        // as one nothing is watching -- the same "silence looks like health" failure at a smaller
        // scale. Admitted rather than dropped, because refusing everything unsequenced would break
        // every source that is not a peer.
        var filter = new DuplicateFilter();

        filter.Admit(Node, null, null).Should().BeTrue();
        filter.Admit(Node, Epoch, null).Should().BeTrue();
        filter.Admit(Node, null, 4).Should().BeTrue();

        filter.Unsequenced.Should().Be(3);
        filter.Admitted.Should().Be(0, "nothing was checked, so nothing was admitted on its merits");
        filter.Duplicates.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void PastTheWindowADuplicateIsAdmittedRatherThanASampleBeingLost()
    {
        // Bounded memory has to fail in one direction or the other. Admitting a stale duplicate
        // inflates a total; dropping a real sample destroys an observation nobody can recover. The
        // window is sized above any buffer a sender is expected to hold, and past it this fails the
        // recoverable way -- stated here rather than discovered on a host that has been up a month.
        var filter = new DuplicateFilter(window: 4);

        for (int seq = 1; seq <= 4; seq++) filter.Admit(Node, Epoch, seq);
        filter.Admit(Node, Epoch, 1).Should().BeFalse("still inside the window");

        for (int seq = 5; seq <= 8; seq++) filter.Admit(Node, Epoch, seq);
        filter.Admit(Node, Epoch, 1).Should().BeTrue("pushed out of the window, and taken again");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheNumberOfSendersTrackedIsBoundedAndTheEvictionsAreCounted()
    {
        // Cardinality an outsider controls: a peer varying its epoch could otherwise grow this
        // without limit. Bounded, and the count of what fell off is reported rather than silent.
        var filter = new DuplicateFilter(senders: 4);

        for (int i = 0; i < 12; i++) filter.Admit($"NODE-{i}", Epoch, 1);

        filter.TrackedSenders.Should().BeLessThanOrEqualTo(4);
        filter.SenderEvictions.Should().BeGreaterThan(0,
            "a bound that discards without saying so is indistinguishable from one that never fills");
    }
}
