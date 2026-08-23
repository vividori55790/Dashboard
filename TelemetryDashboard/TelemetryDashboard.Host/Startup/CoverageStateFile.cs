using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using TelemetryDashboard.Core.Services;

namespace TelemetryDashboard.Host.Startup;

/// <summary>One remembered node, and when this hub last heard from it.</summary>
/// <param name="Node">Stable node id, as it arrived on the wire.</param>
/// <param name="LastHeard">Its newest sample's time, or null if it was only ever declared.</param>
public sealed record CoverageStateEntry(
    [property: JsonPropertyName("node")] string Node,
    [property: JsonPropertyName("lastHeard")] DateTimeOffset? LastHeard);

/// <summary>
/// Remembering which nodes this hub has heard from, across restarts.
/// </summary>
/// <remarks>
/// <c>CoverageLedger</c> learns a node the first time it speaks, which is what lets a fleet be
/// watched without anybody maintaining a list — and the learned set lived only in memory. So a
/// restart forgot that a node had ever existed, and its absence stopped being reported at exactly
/// the moment somebody restarted the hub to find out why data was missing.
/// <para>
/// The time is stored as well as the id. Ids alone bring every remembered node back as "never
/// seen", which reads as hardware that was never commissioned rather than hardware that stopped
/// yesterday — and those two call for opposite things from whoever reads the report.
/// </para>
/// <para>
/// Written without a byte-order mark so anything strict can read it, and rewritten whole, so there
/// is no partial update to get wrong.
/// </para>
/// </remarks>
public static class CoverageStateFile
{
    /// <summary>Nodes recorded in <paramref name="path"/>, or none when it cannot be read.</summary>
    /// <remarks>
    /// A file that is absent, empty or unreadable yields nothing rather than throwing. Losing the
    /// remembered set degrades the hub to learning from scratch, which is where it was before this
    /// existed; refusing to start over it would trade a smaller problem for a bigger one.
    /// </remarks>
    public static IReadOnlyList<CoverageStateEntry> Read(string? path, out string? note)
    {
        note = null;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return [];

        try
        {
            CoverageStateEntry[]? entries = JsonSerializer.Deserialize<CoverageStateEntry[]>(
                File.ReadAllText(path, Utf8Files.WithoutBom));

            return entries?.Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Node)).ToList() ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            note = $"coverage state at {path} could not be read ({ex.Message}); starting from an empty fleet.";
            return [];
        }
    }

    /// <summary>Writes <paramref name="fleet"/> to <paramref name="path"/>, or says why it could not.</summary>
    public static string? Write(string? path, IReadOnlyList<KeyValuePair<string, DateTimeOffset?>> fleet)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        ArgumentNullException.ThrowIfNull(fleet);

        try
        {
            CoverageStateEntry[] entries = fleet
                .OrderBy(node => node.Key, StringComparer.OrdinalIgnoreCase)
                .Select(node => new CoverageStateEntry(node.Key, node.Value))
                .ToArray();

            File.WriteAllText(
                path,
                JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }),
                Utf8Files.WithoutBom);

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"coverage state could not be written to {path}: {ex.Message}";
        }
    }
}
