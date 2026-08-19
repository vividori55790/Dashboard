using System;
using System.Collections.Generic;
using System.Text.Json;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// Splits a catalogue document into the individual manifest objects it contains.
/// </summary>
/// <remarks>
/// Deserialising the whole array in one call would be shorter, but a single malformed entry then
/// throws and the entire catalogue is lost. Extensions come from third parties, so that failure
/// mode lets any one publisher break the listing for everyone. Splitting first lets each manifest
/// be parsed — and rejected — on its own.
/// </remarks>
internal static class ManifestIndexSplitter
{
    /// <summary>
    /// Returns the raw JSON of each element, for an array or for a single object. Returns nothing
    /// when the document is not valid JSON at all — that is a broken catalogue, not a broken entry.
    /// </summary>
    internal static IReadOnlyList<string> SplitTopLevelObjects(string json)
    {
        var entries = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) return entries;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);

            switch (document.RootElement.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (JsonElement element in document.RootElement.EnumerateArray())
                    {
                        entries.Add(element.GetRawText());
                    }
                    break;

                case JsonValueKind.Object:
                    // A catalogue holding exactly one extension is often published unwrapped.
                    entries.Add(document.RootElement.GetRawText());
                    break;
            }
        }
        catch (JsonException)
        {
            // Caller sees an empty catalogue rather than a partial one assembled from guesses.
        }

        return entries;
    }
}
