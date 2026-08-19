using System;
using System.Text.Json;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Recording;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// Projects an arbitrary telemetry JSON frame onto the DVR timeline.
/// </summary>
/// <remarks>
/// Channel discovery is structural, not a hard-coded field list. The previous recorder looked for
/// <c>temp</c>, <c>humidity</c>, <c>vibration</c> and <c>rpm</c> by name, so any deployment that
/// was not the bundled demo — a Modbus power meter, a CAN node, a user's own schema — recorded a
/// blank timeline while appearing to work.
/// </remarks>
public static class TelemetryFrameRecorder
{
    /// <summary>Field names carrying the anomaly score, in priority order.</summary>
    private static readonly string[] AnomalyScoreFields = { "anomalyScore", "zScore", "z_score" };

    /// <summary>Field names identifying the emitting node, in priority order.</summary>
    private static readonly string[] NodeIdFields = { "nodeId", "device", "port", "topic", "source" };

    /// <summary>Fields that describe the frame rather than measure anything.</summary>
    private static readonly string[] NonMeasurementFields =
    {
        "timestamp", "time", "ts", "type", "scenario", "protocol", "unit", "mode", "status"
    };

    public const double DefaultAnomalyThreshold = 2.5;

    /// <summary>Records every numeric leaf of <paramref name="json"/> as its own DVR channel.</summary>
    public static void Record(TimeTravelDvrPlayer dvr, string json, double anomalyThreshold = DefaultAnomalyThreshold)
    {
        if (dvr is null || string.IsNullOrWhiteSpace(json)) return;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return;

            string node = ResolveNodeId(document.RootElement);
            double? score = ResolveAnomalyScore(document.RootElement);
            double timestamp = TimeTravelDvrPlayer.UtcNowSeconds();

            RecordObject(dvr, document.RootElement, node, score, anomalyThreshold, timestamp, depth: 0);
        }
        catch (JsonException)
        {
            // A frame that is not JSON simply has no channels to record.
        }
    }

    private static void RecordObject(
        TimeTravelDvrPlayer dvr,
        JsonElement element,
        string channelPrefix,
        double? inheritedScore,
        double threshold,
        double timestamp,
        int depth)
    {
        if (depth > 4) return; // guard against pathologically nested frames

        double? score = depth == 0 ? inheritedScore : ResolveAnomalyScore(element) ?? inheritedScore;
        string prefix = depth == 0 ? channelPrefix : $"{channelPrefix}";

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (IsNonMeasurement(property.Name)) continue;

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Number when property.Value.TryGetDouble(out double value):
                    if (IsMetadataField(property.Name)) break;

                    // A frame that carried no score field is recorded without a verdict rather
                    // than with a verdict of zero: the producer never judged this sample, and
                    // replaying it as "0.0 sigma, normal" would invent the judgement. The score
                    // that was present came from upstream, so it is attributed as unidentified —
                    // real, but not reproducible by this system's own analyzer.
                    dvr.RecordFrame(
                        $"{prefix}.{property.Name}",
                        value,
                        score ?? 0.0,
                        score.HasValue && score.Value >= threshold,
                        timestamp,
                        score.HasValue ? DvrFrame.UnidentifiedAnalyzer : null);
                    break;

                case JsonValueKind.Object:
                    string nested = ResolveNodeId(property.Value, property.Name);
                    RecordObject(dvr, property.Value, $"{prefix}.{nested}", score, threshold, timestamp, depth + 1);
                    break;
            }
        }
    }

    private static string ResolveNodeId(JsonElement element, string fallback = "TELEMETRY")
    {
        foreach (string field in NodeIdFields)
        {
            if (element.TryGetProperty(field, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String)
            {
                string? text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }
        return fallback;
    }

    /// <summary>
    /// The anomaly score carried by the frame, or <c>null</c> when it carries none.
    /// </summary>
    /// <remarks>
    /// Returns a nullable rather than defaulting to 0.0 so callers can tell "the producer scored
    /// this sample at zero" apart from "the producer never scored it". Collapsing the two turns a
    /// missing judgement into a confident one at the moment it reaches the operator.
    /// </remarks>
    private static double? ResolveAnomalyScore(JsonElement element)
    {
        foreach (string field in AnomalyScoreFields)
        {
            if (element.TryGetProperty(field, out JsonElement value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out double score))
            {
                return score;
            }
        }
        return null;
    }

    /// <summary>The anomaly score itself is metadata about a channel, not a channel to plot.</summary>
    private static bool IsMetadataField(string name)
    {
        foreach (string field in AnomalyScoreFields)
        {
            if (string.Equals(name, field, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsNonMeasurement(string name)
    {
        foreach (string field in NonMeasurementFields)
        {
            if (string.Equals(name, field, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
