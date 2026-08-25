namespace TelemetryDashboard.Core.Models;

/// <summary>One node, and how far its clock is from this host's.</summary>
/// <remarks>
/// A named pair rather than a tuple because it crosses into a JSON payload, where the field names
/// are the contract and a positional tuple would serialise as Item1 and Item2.
/// </remarks>
/// <param name="NodeId">The node as this host knows it, marker prefix and all.</param>
/// <param name="Offset">The estimate, carrying its own error bar or the absence of one.</param>
public readonly record struct NodeClock(string NodeId, ClockOffsetEstimate Offset);
