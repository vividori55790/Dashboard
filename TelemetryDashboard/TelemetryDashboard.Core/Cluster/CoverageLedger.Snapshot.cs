using System;
using System.Collections.Generic;
using System.Linq;

namespace TelemetryDashboard.Core.Cluster;

/// <summary>
/// Turning the ledger's entries into the picture an interface shows.
/// </summary>
/// <remarks>
/// Split from the bookkeeping because the two answer different questions and change for different
/// reasons: above is what the hub was told and heard, here is how that reads at one instant. The
/// ordering is part of the answer — missing nodes first — since a fleet report that buries the two
/// silent nodes under nine hundred healthy ones has hidden the only rows worth looking at.
/// </remarks>
public sealed partial class CoverageLedger
{
    /// <summary>The current picture: who is reporting, who is missing, and for how long.</summary>
    public CoverageSnapshot Snapshot()
    {
        DateTimeOffset now = _clock();

        lock (_gate)
        {
            List<NodeCoverage> nodes = _nodes
                .Select(pair => Describe(pair.Key, pair.Value, now))
                .OrderBy(n => n.Presence == NodePresence.Reporting)
                .ThenBy(n => n.NodeId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new CoverageSnapshot(nodes, SilenceThreshold, now);
        }
    }

    private NodeCoverage Describe(string nodeId, Entry entry, DateTimeOffset now)
    {
        if (entry.LastHeard is not { } last)
        {
            return new NodeCoverage(nodeId, null, 0, NodePresence.NeverSeen, null);
        }

        TimeSpan staleness = now - last;
        NodePresence presence = staleness <= SilenceThreshold ? NodePresence.Reporting : NodePresence.Silent;

        return new NodeCoverage(nodeId, last, entry.Samples, presence, staleness);
    }
}
