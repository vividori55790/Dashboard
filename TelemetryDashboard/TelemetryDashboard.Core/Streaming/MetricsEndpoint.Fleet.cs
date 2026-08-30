using TelemetryDashboard.Core.Cluster;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Query;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// What this host knows about other nodes: how far their clocks are, and who is missing.
/// </summary>
/// <remarks>
/// Both blocks are absent in full when nothing on this host is measuring them, matching the two
/// <c>null</c> blocks <c>/api/status</c> sends for the same reason. A host that compares no clocks
/// and a host that compares them and finds them aligned are different facts, and an offset of zero
/// is the second -- the strongest claim available, that two clocks agree exactly.
/// </remarks>
public static partial class MetricsEndpoint
{
    private static void WriteFleet(Document document, TelemetryStreamingServer server)
    {
        WriteClocks(document, server);
        WriteCoverage(document, server);
    }

    /// <summary>
    /// Per-node clock offsets, and the error bar only where one exists.
    /// </summary>
    /// <remarks>
    /// ARCHITECTURE §3's argument survives the trip here or it does not survive at all. An offset
    /// places a sample on a shared timeline; the spread is what says whether two samples can be
    /// <em>ordered</em>, and a single observation supplies an offset and no spread whatever. A zero
    /// exported there would be the tightest error bar in the fleet, produced by the least evidence
    /// available -- so a one-observation node appears in the offset family and not in the spread
    /// family, and a consumer that needs the bound finds nothing rather than finding a lie.
    /// <para>
    /// <c>ObservedClocks</c> already returns only nodes it has measured, so the guard below is
    /// redundant today. It is written anyway: this endpoint's central rule must hold because this
    /// file enforces it, not because a collaborator elsewhere currently happens to.
    /// </para>
    /// </remarks>
    private static void WriteClocks(Document document, TelemetryStreamingServer server)
    {
        if (server.Clocks?.Invoke() is not { } observed) return;

        Family offset = document.Open("node_clock_offset_seconds", "gauge",
            "Seconds to add to a node's clock to reach this host's. Biased upward by however long "
            + "the fastest message took, which one-way traffic cannot separate from the offset.");

        foreach (NodeClock clock in observed)
        {
            if (clock.Offset.HasOffset) offset.Sample(clock.Offset.OffsetSec, "node", clock.NodeId);
        }

        Family spread = document.Open("node_clock_offset_spread_seconds", "gauge",
            "Dispersion of the observations behind that offset, and a LOWER BOUND on its "
            + "uncertainty rather than the whole of it. Absent for a node with a single "
            + "observation, which supplies no error bar at all -- not an error bar of zero.");

        foreach (NodeClock clock in observed)
        {
            // IsBounded rather than a bare null check, because the type draws the distinction
            // already and a non-finite spread is not an error bar either.
            if (clock.Offset is { IsBounded: true, SpreadSec: { } bar }) spread.Sample(bar, "node", clock.NodeId);
        }

        // Not _total: this is the occupancy of a fixed 64-deep window, so it stops rising and can
        // fall. A counter suffix on it would make rate() report a sample rate that is not one.
        Family samples = document.Open("node_clock_observations", "gauge",
            "Clock comparisons currently behind a node's offset, within this host's fixed window.");

        foreach (NodeClock clock in observed) samples.Sample(clock.Offset.Samples, "node", clock.NodeId);
    }

    /// <summary>
    /// Who was expected and who has been heard from.
    /// </summary>
    /// <remarks>
    /// ARCHITECTURE §1 in the one format where it can reach an alert rule. A node that has stopped
    /// reporting produces no data, and so does a node whose sensors read nominal; the ledger is the
    /// only thing that tells them apart, and until now it could only be read by a person looking at
    /// a page.
    /// <para>
    /// The two families below are deliberately different about zero. <c>node_samples_total</c> is
    /// zero for a node never heard from, and that zero is measured -- the ledger expected it and
    /// counted nothing. <c>node_last_heard_timestamp_seconds</c> is <em>absent</em> for the same
    /// node, because there is no instant to report and the epoch is not one: a zero there dates its
    /// last contact to 1970 and invites a subtraction that answers in decades.
    /// </para>
    /// </remarks>
    private static void WriteCoverage(Document document, TelemetryStreamingServer server)
    {
        if (server.Coverage?.Invoke() is not { } fleet) return;

        document.Open("fleet_nodes_expected", "gauge",
            "Nodes this host was told to expect.")
            .Sample(fleet.Nodes.Count);

        document.Open("fleet_nodes_reporting", "gauge",
            "Expected nodes that have been heard from within the silence threshold.")
            .Sample(fleet.Reporting.Count);

        document.Open("fleet_complete", "gauge",
            "1 only when every expected node is currently reporting. An aggregate drawn while this "
            + "is 0 is short of data, and the silent nodes are the ones worth looking at.")
            .Sample(fleet.IsComplete ? 1.0 : 0.0);

        document.Open("fleet_silence_threshold_seconds", "gauge",
            "How long a node may go unheard before this host stops counting it as reporting.")
            .Sample(fleet.SilenceThreshold.TotalSeconds);

        Family reporting = document.Open("fleet_node_reporting", "gauge",
            "1 when an expected node is currently contributing, 0 when it is silent or has never "
            + "been seen. The zero is measured: the node was expected and nothing arrived.");

        foreach (NodeCoverage node in fleet.Nodes)
        {
            reporting.Sample(node.Presence == NodePresence.Reporting ? 1.0 : 0.0, "node", node.NodeId);
        }

        Family heard = document.Open("fleet_node_last_heard_timestamp_seconds", "gauge",
            "When a node's most recent sample arrived, in seconds since the Unix epoch. Absent for "
            + "a node that has never sent anything -- it has no last contact, and the epoch is not "
            + "a stand-in for one.");

        foreach (NodeCoverage node in fleet.Nodes)
        {
            if (node.LastHeard is { } at) heard.Sample(SeriesClock.ToSeconds(at.UtcDateTime), "node", node.NodeId);
        }

        Family contributed = document.Open("fleet_node_samples_total", "counter",
            "Samples a node has contributed since this ledger started.");

        foreach (NodeCoverage node in fleet.Nodes) contributed.Sample(node.Samples, "node", node.NodeId);
    }
}
