using System;
using System.Text.Json.Serialization;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// One telemetry sample as it appears on the WebSocket and SSE streams.
/// </summary>
/// <remarks>
/// Field names match the frames the desktop shell broadcasts, so a console written against one
/// host works against the other unchanged.
///
/// The verdict fields are nullable and omitted when absent. During an analyzer's warm-up there is
/// no baseline yet, so <c>AnomalyResult.ZScore</c> is 0 and <c>IsAnomaly</c> false — which on the
/// wire reads exactly like "measured, and calm". Omitting them lets the recorder downstream store
/// the frame without a verdict instead of promoting an absent judgement into a confident one.
/// </remarks>
public sealed class TelemetryFrame
{
    /// <summary>Prefix applied to the node id of every synthetic frame.</summary>
    /// <remarks>
    /// The DVR timeline records channels as <c>nodeId.field</c> and keeps no origin flag of its
    /// own, so a replay pulled a week later would present synthetic channels exactly like measured
    /// ones. Carrying the mark inside the name is what makes it survive into that recording.
    /// </remarks>
    public const string SimulatedNodePrefix = "SIM:";

    /// <summary>Sample time, ISO 8601.</summary>
    [JsonPropertyName("timestamp")] public string Timestamp { get; init; } = string.Empty;

    /// <summary>Where the frame came from, e.g. <c>REAL_HARDWARE</c> or <c>SIMULATED</c>.</summary>
    [JsonPropertyName("source")] public string Source { get; init; } = string.Empty;

    /// <summary>True when the value was generated rather than measured.</summary>
    [JsonPropertyName("simulated")] public bool Simulated { get; init; }

    /// <summary>Port or stream the raw line arrived on.</summary>
    [JsonPropertyName("port")] public string Port { get; init; } = string.Empty;

    /// <summary>Emitting node, prefixed <c>SIM:</c> when synthetic.</summary>
    [JsonPropertyName("nodeId")] public string NodeId { get; init; } = string.Empty;

    /// <summary>Channel name within the node.</summary>
    [JsonPropertyName("variable")] public string Variable { get; init; } = string.Empty;

    /// <summary>The measured value.</summary>
    [JsonPropertyName("value")] public double Value { get; init; }

    /// <summary>Engineering unit, empty when the device did not send one.</summary>
    [JsonPropertyName("unit")] public string Unit { get; init; } = string.Empty;

    /// <summary>Standard deviations from the analyzer's baseline, absent before a baseline exists.</summary>
    [JsonPropertyName("anomalyScore")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? AnomalyScore { get; init; }

    /// <summary>Whether the analyzer flagged this sample, absent before a baseline exists.</summary>
    [JsonPropertyName("isAnomaly")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsAnomaly { get; init; }

    /// <summary>
    /// Extrapolated value <see cref="PredictedHorizonSec"/> seconds ahead. Absent when the channel's
    /// own history does not reach far enough to support any forecast at all.
    /// </summary>
    /// <remarks>
    /// Never read this without <see cref="PredictedHorizonSec"/>. The horizon is not fixed: it is
    /// as far as the trend can be carried while staying inside the range the channel has occupied,
    /// which for a fast-sampled channel with a short window is often a few seconds rather than a
    /// minute. The field keeps its old name so existing consoles do not break, and the horizon is
    /// published beside it so nobody mistakes eleven seconds of foresight for sixty.
    /// </remarks>
    [JsonPropertyName("predicted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Predicted60s { get; init; }

    /// <summary>How far ahead <see cref="Predicted60s"/> looks, in seconds.</summary>
    [JsonPropertyName("predictedHorizonSec")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? PredictedHorizonSec { get; init; }

    /// <summary>
    /// Present and true when a real trend, continued, would leave the range this channel has
    /// occupied — so no number is given, but the fact is.
    /// </summary>
    /// <remarks>
    /// Absent in the ordinary case, so a consumer sees it only when it means something. It is not
    /// interchangeable with a missing <see cref="Predicted60s"/>: that says "no trend worth
    /// extrapolating", this says "a trend that is heading somewhere implausible".
    /// </remarks>
    [JsonPropertyName("forecastLeavesRange")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ForecastLeavesRange { get; init; }

    /// <summary>Analyzer and settings behind the verdict, so a stored frame can be re-scored.</summary>
    [JsonPropertyName("analyzerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AnalyzerId { get; init; }

    /// <summary>Builds the wire frame for one packet and the verdict reached about it.</summary>
    public static TelemetryFrame Create(
        TelemetryPacket packet,
        AnomalyResult analysis,
        string origin,
        bool simulated,
        string portName)
    {
        bool judged = analysis.HasVerdict;

        return new TelemetryFrame
        {
            Timestamp = packet.Timestamp.ToString("o"),
            Source = origin,
            Simulated = simulated,
            Port = portName,
            NodeId = MarkNode(packet.NodeId, simulated),
            Variable = packet.Variable,
            Value = packet.Value,
            Unit = packet.Unit,
            AnomalyScore = judged ? analysis.ZScore : null,
            IsAnomaly = judged ? analysis.IsAnomaly : null,
            // Independent of the verdict: a channel can be scored confidently and still have no
            // trend worth extrapolating. Tying the two together published a forecast for every
            // judged channel, including the ones that are pure noise.
            Predicted60s = analysis.HasForecast ? analysis.PredictedValueIn60s : null,
            PredictedHorizonSec = analysis.HasForecast ? analysis.ForecastHorizonSec : null,
            ForecastLeavesRange = analysis.ForecastLeavesObservedRange ? true : null,
            AnalyzerId = analysis.AnalyzerId
        };
    }

    /// <summary>Applies the synthetic marker, idempotently.</summary>
    /// <remarks>
    /// The router now marks packets as they are produced so plugins can see it too. Re-marking here
    /// would produce <c>SIM:SIM:</c>, which is a different channel name and would split one
    /// synthetic series into two.
    /// </remarks>
    public static string MarkNode(string nodeId, bool simulated) =>
        simulated ? Core.Models.SimulatedNodeMarker.Apply(nodeId) : nodeId;
}
