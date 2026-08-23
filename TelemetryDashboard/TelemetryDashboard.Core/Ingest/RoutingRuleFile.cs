using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TelemetryDashboard.Core.Ingest;

// The file's own shape, kept apart from the reader that judges it. These say what may be written;
// RoutingRuleReader says what will be honoured, and the two are different questions.

/// <summary>One alias as it is written in the file.</summary>
internal sealed class AliasDto
{
    [JsonPropertyName("channel")] public string? Channel { get; init; }
    [JsonPropertyName("unit")] public string? Unit { get; init; }
    [JsonPropertyName("gain")] public double? Gain { get; init; }
    [JsonPropertyName("offset")] public double? Offset { get; init; }
}

/// <summary>One rule as it is written in the file.</summary>
internal sealed class RuleDto
{
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("tag")] public string? Tag { get; init; }
    [JsonPropertyName("port")] public string? Port { get; init; }
    [JsonPropertyName("node")] public string? Node { get; init; }
    [JsonPropertyName("channels")] public Dictionary<string, AliasDto>? Channels { get; init; }
}

/// <summary>The file itself.</summary>
internal sealed class RoutingRuleFile
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("rules")] public List<RuleDto>? Rules { get; init; }
}
