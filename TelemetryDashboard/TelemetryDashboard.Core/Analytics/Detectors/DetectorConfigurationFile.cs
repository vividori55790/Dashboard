using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TelemetryDashboard.Core.Analytics.Detectors;

/// <summary>Wire shape of an analytics configuration file.</summary>
/// <remarks>
/// Separate from <see cref="DetectorConfiguration"/> and internal, so the format on disk can gain a
/// field without the type the rest of the system is written against changing shape. Every property
/// is nullable because "absent" and "set to zero" are different instructions and the reader has to
/// be able to tell them apart.
/// </remarks>
internal sealed class DetectorConfigurationFile
{
    [JsonPropertyName("detectors")] public List<DetectorEntryFile>? Detectors { get; set; }
    [JsonPropertyName("inference")] public InferenceEntryFile? Inference { get; set; }
}

/// <summary>Wire shape of one detector entry.</summary>
internal sealed class DetectorEntryFile
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("channels")] public List<string>? Channels { get; set; }
    [JsonPropertyName("window")] public int? Window { get; set; }
    [JsonPropertyName("threshold")] public double? Threshold { get; set; }
    [JsonPropertyName("lambda")] public double? Lambda { get; set; }
    [JsonPropertyName("maxRatePerSecond")] public double? MaxRatePerSecond { get; set; }
    [JsonPropertyName("maxGapSeconds")] public double? MaxGapSeconds { get; set; }
    [JsonPropertyName("sampleRateHz")] public double? SampleRateHz { get; set; }
}

/// <summary>Wire shape of the external-model section.</summary>
internal sealed class InferenceEntryFile
{
    [JsonPropertyName("runtime")] public string? Runtime { get; set; }
    [JsonPropertyName("endpoint")] public string? Endpoint { get; set; }
    [JsonPropertyName("modelPath")] public string? ModelPath { get; set; }
    [JsonPropertyName("modelId")] public string? ModelId { get; set; }
    [JsonPropertyName("channels")] public List<string>? Channels { get; set; }
    [JsonPropertyName("window")] public int? Window { get; set; }
    [JsonPropertyName("threshold")] public double? Threshold { get; set; }
    [JsonPropertyName("timeoutMs")] public int? TimeoutMs { get; set; }
    [JsonPropertyName("queueCapacity")] public int? QueueCapacity { get; set; }
    [JsonPropertyName("maxScoreAgeMs")] public int? MaxScoreAgeMs { get; set; }
    [JsonPropertyName("samplesBetweenRequests")] public int? SamplesBetweenRequests { get; set; }
}
