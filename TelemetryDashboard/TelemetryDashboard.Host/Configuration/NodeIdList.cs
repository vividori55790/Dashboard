using System;
using System.Collections.Generic;
using System.Linq;

namespace TelemetryDashboard.Host.Configuration;

/// <summary>
/// Reading a comma-separated list of node ids off the command line.
/// </summary>
/// <remarks>
/// Its own file because two flags read one, and a node id that survives one flag's parsing and not
/// the other's is a fleet the hub half knows about. Duplicates are dropped and case is kept: the
/// ledger compares ids case-insensitively, but what an operator typed is what gets printed back to
/// them, and quietly lower-casing PSFB-01 makes a report harder to match against a rig.
/// </remarks>
public static class NodeIdList
{
    /// <summary>Splits <paramref name="raw"/> on commas, or returns an empty list.</summary>
    public static IReadOnlyList<string> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => seen.Add(id))
            .ToList();
    }
}
