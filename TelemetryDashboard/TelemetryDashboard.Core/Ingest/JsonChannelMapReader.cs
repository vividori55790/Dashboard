using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TelemetryDashboard.Core.Ingest;

/// <summary>Wire shape of a channel map file.</summary>
internal sealed class JsonChannelMapFile
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("nodePath")] public string? NodePath { get; set; }
    [JsonPropertyName("nodeFallback")] public string? NodeFallback { get; set; }
    [JsonPropertyName("channels")] public List<JsonChannelFile>? Channels { get; set; }
}

/// <summary>Wire shape of one channel entry.</summary>
internal sealed class JsonChannelFile
{
    [JsonPropertyName("variable")] public string? Variable { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("unit")] public string? Unit { get; set; }
}

/// <summary>
/// Loads a <see cref="JsonChannelMap"/> from a file, refusing anything it cannot honour.
/// </summary>
/// <remarks>
/// Every failure here is loud. A map is the only thing standing between a feed and a chart, and a
/// map that half-loaded would produce a dashboard that is quietly missing channels — which looks
/// exactly like a feed that is quietly missing data. Refusing to start is the kinder outcome:
/// the operator finds out now, at the moment they can fix it, rather than during an incident.
/// </remarks>
public static class JsonChannelMapReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Reads a map from disk.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file exists but does not describe a usable map.</exception>
    public static JsonChannelMap Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Channel map '{path}' was not found.", path);
        }

        return Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>Parses a map from text, so the format can be tested without touching a disk.</summary>
    public static JsonChannelMap Parse(string json, string fallbackName = "channel-map")
    {
        JsonChannelMapFile? file;

        try
        {
            file = JsonSerializer.Deserialize<JsonChannelMapFile>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Channel map is not valid JSON: {ex.Message}", ex);
        }

        if (file?.Channels is null || file.Channels.Count == 0)
        {
            throw new InvalidDataException(
                "A channel map must declare at least one channel. A map with none would connect to "
                + "the feed, read every event, and produce nothing, which is indistinguishable from "
                + "a feed that has stopped.");
        }

        var channels = new List<JsonChannel>(file.Channels.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (JsonChannelFile entry in file.Channels)
        {
            if (string.IsNullOrWhiteSpace(entry.Variable) || string.IsNullOrWhiteSpace(entry.Path))
            {
                throw new InvalidDataException(
                    "Every channel needs both a 'variable' and a 'path'; one without a path can never match.");
            }

            if (!seen.Add(entry.Variable))
            {
                throw new InvalidDataException(
                    $"Channel '{entry.Variable}' is declared twice. Two paths writing one channel name "
                    + "would interleave two quantities into a single series that looks like noise.");
            }

            channels.Add(new JsonChannel(entry.Variable.Trim(), entry.Path.Trim(), entry.Unit?.Trim() ?? string.Empty));
        }

        return new JsonChannelMap(
            string.IsNullOrWhiteSpace(file.Name) ? fallbackName : file.Name.Trim(),
            channels,
            string.IsNullOrWhiteSpace(file.NodePath) ? null : file.NodePath.Trim(),
            string.IsNullOrWhiteSpace(file.NodeFallback) ? fallbackName : file.NodeFallback.Trim());
    }
}
