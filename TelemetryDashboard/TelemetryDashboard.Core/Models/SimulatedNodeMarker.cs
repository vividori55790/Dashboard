using System;

namespace TelemetryDashboard.Core.Models;

/// <summary>
/// The prefix that keeps synthetic data identifiable wherever it travels.
/// </summary>
/// <remarks>
/// A single definition, because the mark was previously applied in one place and re-applied in
/// another, and the two did not agree about who had already done it. Applying it twice yields
/// <c>SIM:SIM:NODE</c>, which is not merely ugly: it is a different channel name, so a synthetic
/// series would split in two the moment a second marker was added anywhere on the path.
/// </remarks>
public static class SimulatedNodeMarker
{
    /// <summary>Prefix identifying a node whose data was generated rather than measured.</summary>
    public const string Prefix = "SIM:";

    /// <summary>Applies the prefix. Idempotent: marking an already-marked node changes nothing.</summary>
    public static string Apply(string? nodeId)
    {
        string value = nodeId ?? string.Empty;
        return value.StartsWith(Prefix, StringComparison.Ordinal) ? value : Prefix + value;
    }

    /// <summary>Whether this node id is already marked as synthetic.</summary>
    public static bool IsMarked(string? nodeId) =>
        nodeId is not null && nodeId.StartsWith(Prefix, StringComparison.Ordinal);
}
