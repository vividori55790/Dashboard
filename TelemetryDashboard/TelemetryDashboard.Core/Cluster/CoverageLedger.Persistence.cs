using System;
using System.Collections.Generic;
using System.Linq;

namespace TelemetryDashboard.Core.Cluster;

/// <summary>
/// What a ledger hands over so the next run can start where this one left off.
/// </summary>
/// <remarks>
/// Together because they are one decision seen twice: the ids alone are enough to keep expecting a
/// node, and only the history distinguishes hardware that has never worked from hardware that
/// stopped. A restore from ids alone brings every remembered node back as never seen, which reads
/// as a fleet that was never commissioned.
/// </remarks>
public sealed partial class CoverageLedger
{
    /// <summary>Every node this ledger knows about, declared or learned. Persist this.</summary>
    public IReadOnlyList<string> KnownNodes
    {
        get { lock (_gate) return _nodes.Keys.ToList(); }
    }

    /// <summary>Every node this ledger knows about, with when it was last heard from.</summary>
    /// <remarks>
    /// The shape to persist. <see cref="KnownNodes"/> carries ids alone and loses the one fact that
    /// distinguishes a node that has never worked from one that stopped.
    /// </remarks>
    public IReadOnlyList<KeyValuePair<string, DateTimeOffset?>> KnownNodeHistory
    {
        get
        {
            lock (_gate)
            {
                return _nodes
                    .Select(pair => new KeyValuePair<string, DateTimeOffset?>(pair.Key, pair.Value.LastHeard))
                    .ToList();
            }
        }
    }
}
