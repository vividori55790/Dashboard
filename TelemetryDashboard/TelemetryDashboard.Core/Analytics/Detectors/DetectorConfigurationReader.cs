using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TelemetryDashboard.Core.Analytics.Detectors;

/// <summary>
/// Loads an analytics configuration from a file, refusing anything it cannot honour.
/// </summary>
/// <remarks>
/// Loud for the reason <c>JsonChannelMapReader</c> is loud. A half-loaded detector set produces a
/// host that is monitoring less than the operator believes it is, and there is no symptom: the
/// charts are identical, and the alerts that never fire look like alerts that had nothing to fire
/// about. Refusing to start says it at the one moment it can still be fixed. A file that is simply
/// absent is not an error — it means no extra detectors were configured, which is a legitimate
/// state and the one this system shipped in.
/// </remarks>
public static class DetectorConfigurationReader
{
    /// <summary>Name looked for beside the executable when no path is given.</summary>
    public const string DefaultFileName = "detectors.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>The kinds <see cref="DetectorFactory"/> can build.</summary>
    public static IReadOnlyList<string> KnownKinds { get; } = new[] { "mad", "ewma", "rate", "zscore" };

    /// <summary>Reads a configuration, or <see cref="DetectorConfiguration.None"/> when absent.</summary>
    public static DetectorConfiguration LoadOrNone(string path) =>
        string.IsNullOrWhiteSpace(path) || !File.Exists(path)
            ? DetectorConfiguration.None
            : Parse(File.ReadAllText(path));

    /// <summary>Parses a configuration from text, so the format can be tested without touching a disk.</summary>
    public static DetectorConfiguration Parse(string json)
    {
        DetectorConfigurationFile? file;
        try
        {
            file = JsonSerializer.Deserialize<DetectorConfigurationFile>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Analytics configuration is not valid JSON: {ex.Message}", ex);
        }

        if (file is null) return DetectorConfiguration.None;

        return new DetectorConfiguration
        {
            Detectors = (file.Detectors ?? new List<DetectorEntryFile>()).Select(ReadDetector).ToArray(),
            Inference = file.Inference is null ? null : ReadInference(file.Inference)
        };
    }

    private static DetectorSpec ReadDetector(DetectorEntryFile entry)
    {
        string kind = (entry.Type ?? string.Empty).Trim().ToLowerInvariant();

        if (!KnownKinds.Contains(kind))
        {
            throw new InvalidDataException(
                $"Detector type '{entry.Type}' is not one this build can construct. Known types: "
                + string.Join(", ", KnownKinds) + ". A type nobody recognises would be read, "
                + "accepted and never asked anything.");
        }

        if (kind == "rate" && !(entry.MaxRatePerSecond > 0))
        {
            throw new InvalidDataException(
                "A 'rate' detector needs a positive 'maxRatePerSecond'. It judges against a physical "
                + "limit rather than the channel's own history, so there is no value to infer.");
        }

        var defaults = new DetectorSpec();
        return new DetectorSpec
        {
            Kind = kind,
            Label = string.IsNullOrWhiteSpace(entry.Id) ? null : entry.Id!.Trim(),
            Channels = ReadChannels(entry.Channels, "detector '" + (entry.Id ?? kind) + "'"),
            Window = entry.Window ?? defaults.Window,
            Threshold = entry.Threshold ?? defaults.Threshold,
            Lambda = entry.Lambda ?? defaults.Lambda,
            MaxRatePerSecond = entry.MaxRatePerSecond ?? 0,
            MaxGapSeconds = entry.MaxGapSeconds ?? defaults.MaxGapSeconds,
            SampleRateHz = entry.SampleRateHz ?? defaults.SampleRateHz
        };
    }

    private static InferenceSpec ReadInference(InferenceEntryFile entry)
    {
        string runtime = (entry.Runtime ?? string.Empty).Trim().ToLowerInvariant();
        InferenceRuntime parsed = runtime switch
        {
            "" or "none" => InferenceRuntime.None,
            "http" or "remote" => InferenceRuntime.Http,
            "onnx" or "inprocess" => InferenceRuntime.InProcess,
            _ => throw new InvalidDataException(
                $"Inference runtime '{entry.Runtime}' is not recognised. Use 'http', 'onnx' or 'none'.")
        };

        if (parsed == InferenceRuntime.Http && string.IsNullOrWhiteSpace(entry.Endpoint))
        {
            throw new InvalidDataException("An 'http' inference runtime needs an 'endpoint' to POST windows to.");
        }

        if (parsed == InferenceRuntime.InProcess && string.IsNullOrWhiteSpace(entry.ModelPath))
        {
            throw new InvalidDataException("An 'onnx' inference runtime needs a 'modelPath' to load.");
        }

        var defaults = new InferenceSpec();
        return new InferenceSpec
        {
            Runtime = parsed,
            Endpoint = entry.Endpoint?.Trim(),
            ModelPath = entry.ModelPath?.Trim(),
            ModelId = string.IsNullOrWhiteSpace(entry.ModelId) ? defaults.ModelId : entry.ModelId!.Trim(),
            Channels = ReadChannels(entry.Channels, "the inference section"),
            Window = entry.Window ?? defaults.Window,
            Threshold = entry.Threshold ?? defaults.Threshold,
            TimeoutMs = entry.TimeoutMs ?? defaults.TimeoutMs,
            QueueCapacity = entry.QueueCapacity ?? defaults.QueueCapacity,
            MaxScoreAgeMs = entry.MaxScoreAgeMs ?? defaults.MaxScoreAgeMs,
            SamplesBetweenRequests = entry.SamplesBetweenRequests ?? defaults.SamplesBetweenRequests
        };
    }

    /// <summary>Absent means every channel; explicitly empty is a mistake and is refused.</summary>
    private static IReadOnlyList<string> ReadChannels(List<string>? channels, string owner)
    {
        if (channels is null) return new[] { ChannelSelector.MatchAll };

        if (channels.Count == 0)
        {
            throw new InvalidDataException(
                $"'channels' on {owner} is present but empty. That builds a detector, counts it in "
                + "the report and never asks it anything; omit the field to watch every channel.");
        }

        return channels;
    }
}
