using System;
using System.Collections.Generic;
using System.Linq;

namespace TelemetryDashboard.Core.Cluster;

/// <summary>Whether a node the system expects to hear from is actually being heard from.</summary>
public enum NodePresence
{
    /// <summary>Sent something recently enough to be considered live.</summary>
    Reporting,

    /// <summary>Has been heard from before, but not within the silence threshold.</summary>
    Silent,

    /// <summary>Expected — declared in configuration — but has never sent anything.</summary>
    NeverSeen
}

/// <summary>What is known about one node's contribution to a view.</summary>
/// <param name="NodeId">Stable installation identifier.</param>
/// <param name="LastHeard">When its most recent sample arrived, or null if it never has.</param>
/// <param name="Samples">How many samples it has contributed since this ledger started.</param>
/// <param name="Presence">Live, silent, or never seen at all.</param>
/// <param name="Staleness">Age of its newest sample, or null if it never sent one.</param>
public sealed record NodeCoverage(
    string NodeId,
    DateTimeOffset? LastHeard,
    long Samples,
    NodePresence Presence,
    TimeSpan? Staleness);

/// <summary>
/// Which nodes a view actually drew on, and which ones it is missing.
/// </summary>
/// <remarks>
/// This is the type that makes a distributed answer honest, and it exists because the failure it
/// prevents is invisible without it. A dashboard aggregating a thousand nodes while two of them
/// have gone quiet draws a chart that looks entirely healthy: the missing data does not appear as a
/// gap, it appears as slightly fewer contributions to an average nobody is checking the denominator
/// of. The two silent nodes are the ones worth looking at, and the interface has hidden them.
///
/// So no aggregate is allowed to travel without one of these attached. A number that does not know
/// what it is missing is an impression, not a measurement.
/// </remarks>
public sealed record CoverageSnapshot(
    IReadOnlyList<NodeCoverage> Nodes,
    TimeSpan SilenceThreshold,
    DateTimeOffset TakenAt)
{
    /// <summary>Nodes that are currently contributing.</summary>
    public IReadOnlyList<NodeCoverage> Reporting =>
        Nodes.Where(n => n.Presence == NodePresence.Reporting).ToList();

    /// <summary>Nodes that were expected and are not contributing.</summary>
    /// <remarks>Both kinds of absence, because both mean the view is short of data.</remarks>
    public IReadOnlyList<NodeCoverage> Missing =>
        Nodes.Where(n => n.Presence != NodePresence.Reporting).ToList();

    /// <summary>True only when every expected node is currently reporting.</summary>
    public bool IsComplete => Missing.Count == 0;

    /// <summary>Reporting nodes as a fraction of expected, or 1.0 when nothing is expected.</summary>
    /// <remarks>
    /// Deliberately not called "health". It measures how much of the fleet answered, which is a
    /// different question from whether the fleet is well, and conflating them is how an outage
    /// comes to be displayed as a slightly lower score.
    /// </remarks>
    public double ExpectedFraction => Nodes.Count == 0 ? 1.0 : (double)Reporting.Count / Nodes.Count;

    /// <summary>
    /// One sentence an interface can show verbatim.
    /// </summary>
    /// <remarks>
    /// Phrased so the incomplete case cannot be skimmed as the complete one. "998 of 1000" reads as
    /// almost everything; naming the missing nodes reads as something to go and look at.
    /// </remarks>
    public string Describe()
    {
        if (Nodes.Count == 0) return "No nodes are expected yet, so nothing is missing.";

        if (IsComplete)
        {
            return $"All {Nodes.Count} expected node(s) reporting as of {TakenAt:HH:mm:ss}.";
        }

        IReadOnlyList<NodeCoverage> missing = Missing;
        string named = string.Join(", ", missing.Take(5).Select(Name));
        string more = missing.Count > 5 ? $", and {missing.Count - 5} more" : string.Empty;

        return $"Incomplete: {Reporting.Count} of {Nodes.Count} node(s) reporting. "
               + $"Missing {named}{more}. Figures below exclude them.";
    }

    private static string Name(NodeCoverage node) => node.Presence == NodePresence.NeverSeen
        ? $"{node.NodeId} (never seen)"
        : $"{node.NodeId} (silent {Format(node.Staleness)})";

    private static string Format(TimeSpan? age) => age switch
    {
        null => "for an unknown time",
        { TotalSeconds: < 90 } value => $"{value.TotalSeconds:0}s",
        { TotalMinutes: < 90 } value => $"{value.TotalMinutes:0}m",
        { TotalHours: < 48 } value => $"{value.TotalHours:0}h",
        { } value => $"{value.TotalDays:0}d"
    };
}
