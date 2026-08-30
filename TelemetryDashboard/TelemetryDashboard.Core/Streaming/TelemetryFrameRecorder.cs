using System;
using System.Text.Json;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Recording;
using TelemetryDashboard.Core.Query;

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
    /// <remarks>
    /// <c>seq</c> and <c>epoch</c> are transport bookkeeping — who sent this and in what order —
    /// and were added when exchange became idempotent. <c>seq</c> is numeric, so without this it
    /// became a DVR channel counting upward forever beside every real one, which an existing test
    /// caught by finding two frames recorded for one sample. <c>epoch</c> is a string and would be
    /// skipped anyway; naming it is what makes that deliberate rather than lucky, since the day it
    /// becomes a number is the day it starts being plotted.
    /// <para>
    /// Not listed, and deliberately: <c>lateBySec</c>. How old a reading was when it arrived is a
    /// property of that reading, exactly as the score and the forecast beside it are, and it is
    /// worth having in a replay pulled a week later.
    /// </para>
    /// </remarks>
    private static readonly string[] NonMeasurementFields =
    {
        "timestamp", "time", "ts", "type", "scenario", "protocol", "unit", "mode", "status",
        "seq", "epoch"
    };

    public const double DefaultAnomalyThreshold = 2.5;

    /// <summary>Records every numeric leaf of <paramref name="json"/> as its own DVR channel.</summary>
    /// <param name="series">
    /// Optional rolling store fed from the same parse, so the query API sees the same channels the
    /// DVR does without a second pass over the frame.
    /// </param>
    public static void Record(
        TimeTravelDvrPlayer dvr,
        string json,
        double anomalyThreshold = DefaultAnomalyThreshold,
        SeriesStore? series = null)
    {
        if (dvr is null || string.IsNullOrWhiteSpace(json)) return;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return;

            // The channel a frame describes is its node *and* its variable. Keying on the node
            // alone collapsed every channel of a profile into one series: a four-channel run wrote
            // temperature, humidity, vibration and speed all into "<node>.value", interleaved, and
            // /api/series served that mixture to any browser drawing a chart. It was visible in the
            // arithmetic -- the merged series reported 36 Hz for a 10 Hz channel, and its spectrum
            // peaked at exactly half Nyquist, which is what alternating unrelated quantities look
            // like. Including the variable makes the key match the one the analytics engine already
            // uses for the same sample.
            string node = ResolveNodeId(document.RootElement);
            string channel = ResolveVariable(document.RootElement) is { } variable
                ? $"{node}.{variable}"
                : node;

            double? score = ResolveAnomalyScore(document.RootElement);
            double timestamp = TimeTravelDvrPlayer.UtcNowSeconds();

            // Arrival time, not measurement time: this path receives frames whose own timestamp
            // field is not trusted to be a clock the server shares. A producer that knows better
            // should call TelemetryStreamingServer.PublishSample with the sample's own timestamp.
            var sink = series is null ? default : new SeriesSink(series, SeriesClock.UtcNowSec());

            RecordObject(dvr, document.RootElement, channel, score, anomalyThreshold, timestamp, depth: 0, sink);
        }
        catch (JsonException)
        {
            // A frame that is not JSON simply has no channels to record.
        }
    }

    /// <summary>Where the series store writes go, and the instant they are stamped with.</summary>
    private readonly record struct SeriesSink(SeriesStore? Store, double TimestampSec)
    {
        public void Append(string channel, double value) => Store?.Append(channel, value, TimestampSec);
    }

    /// <summary>The variable a per-channel frame names, or null when it names none.</summary>
    /// <remarks>
    /// Only the canonical per-channel frame carries this. An aggregate frame -- several quantities
    /// in one object -- has no single variable, and falls back to keying by node and field name,
    /// which is correct for that shape because the field name <em>is</em> the quantity there.
    /// </remarks>
    private static string? ResolveVariable(JsonElement element) =>
        element.TryGetProperty("variable", out JsonElement variable)
        && variable.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(variable.GetString())
            ? variable.GetString()
            : null;

    /// <summary>Whether a numeric field is the reading itself rather than something about it.</summary>
    private static bool IsMeasurementField(string name) =>
        string.Equals(name, "value", StringComparison.OrdinalIgnoreCase);

    private static void RecordObject(
        TimeTravelDvrPlayer dvr,
        JsonElement element,
        string channelPrefix,
        double? inheritedScore,
        double threshold,
        double timestamp,
        int depth,
        SeriesSink sink)
    {
        if (depth > 4) return; // guard against pathologically nested frames

        double? score = depth == 0 ? inheritedScore : ResolveAnomalyScore(element) ?? inheritedScore;
        bool namesAVariable = ResolveVariable(element) is not null;
        string prefix = depth == 0 ? channelPrefix : $"{channelPrefix}";

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (IsNonMeasurement(property.Name)) continue;

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Number when property.Value.TryGetDouble(out double value):
                    if (IsMetadataField(property.Name)) break;

                    // "value" is the measurement, so it is the channel; anything else beside it --
                    // a score, a forecast -- is a property of that channel and keeps its own name.
                    // Two different questions, and collapsing them into one flag collapsed every
                    // channel of an aggregate frame back into a single series -- the interleaving
                    // this key was built to stop. The key asks which field IS the channel:
                    // 'value' names the channel itself, any other field names one beside it.
                    string seriesKey = IsMeasurementField(property.Name)
                        ? prefix
                        : $"{prefix}.{property.Name}";

                    // The verdict asks which fields are readings at all, and that depends on the
                    // frame's shape. ResolveVariable already draws the line: a frame naming a
                    // variable has exactly one reading, and everything numeric beside it is a
                    // property of that reading. An aggregate frame names none, and there the field
                    // name is the quantity, so every numeric field is a reading.
                    bool isAReading = !namesAVariable || IsMeasurementField(property.Name);

                    // And the verdict belongs to the readings alone. It used to be written onto
                    // every numeric field, so a replay showed a forecast flagged as anomalous
                    // whenever its channel was, carrying that channel's sigma as its own
                    // -- a judgement about one quantity presented as a judgement about another,
                    // which is the shape §7 names for a peer's score and reached here inside one
                    // process by a loop that had one score in scope and several values.
                    double? verdict = isAReading ? score : null;

                    sink.Append(seriesKey, value);

                    // A frame that carried no score field is recorded without a verdict rather
                    // than with a verdict of zero: the producer never judged this sample, and
                    // replaying it as "0.0 sigma, normal" would invent the judgement. The score
                    // that was present came from upstream, so it is attributed as unidentified —
                    // real, but not reproducible by this system's own analyzer.
                    dvr.RecordFrame(
                        seriesKey,
                        value,
                        verdict ?? 0.0,
                        verdict.HasValue && verdict.Value >= threshold,
                        timestamp,
                        verdict.HasValue ? DvrFrame.UnidentifiedAnalyzer : null);
                    break;

                case JsonValueKind.Object:
                    string nested = ResolveNodeId(property.Value, property.Name);
                    RecordObject(dvr, property.Value, $"{prefix}.{nested}", score, threshold, timestamp, depth + 1, sink);
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
