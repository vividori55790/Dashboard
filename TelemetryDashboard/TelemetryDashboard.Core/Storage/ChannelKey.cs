using System;

namespace TelemetryDashboard.Core.Storage;

/// <summary>
/// Identifies one telemetry series: a variable on a node.
/// </summary>
/// <remarks>
/// A value type with structural equality, because it is the dictionary key every rollup bucket and
/// every compressed block is filed under. At the scale this store is built for — a million series —
/// the key is looked up once per sample, so a reference type here would mean a million allocations
/// per second of ingest and a comparer call chain on every one of them.
/// <para>
/// Comparison is ordinal and case-sensitive, matching <c>SqliteTelemetryQuery</c>: node and channel
/// identifiers arrive from firmware as exact tokens, and folding case here would merge two series
/// that the raw store keeps apart.
/// </para>
/// </remarks>
public readonly record struct ChannelKey(string NodeId, string Variable)
{
    /// <summary>Builds a key, mapping a null identifier to the empty string the raw store writes.</summary>
    public static ChannelKey From(string? nodeId, string? variable) =>
        new(nodeId ?? string.Empty, variable ?? string.Empty);

    /// <summary>Human-readable form, for prune reports and diagnostics.</summary>
    public override string ToString() => $"{NodeId}/{Variable}";
}
