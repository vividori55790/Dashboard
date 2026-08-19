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

    /// <summary>Extrapolated value 60 seconds ahead, absent before a baseline exists.</summary>
    [JsonPropertyName("predicted60s")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Predicted60s { get; init; }

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
            Predicted60s = judged ? analysis.PredictedValueIn60s : null,
            AnalyzerId = analysis.AnalyzerId
        };
    }

    /// <summary>Applies <see cref="SimulatedNodePrefix"/> to synthetic node ids.</summary>
    public static string MarkNode(string nodeId, bool simulated) =>
        simulated ? SimulatedNodePrefix + nodeId : nodeId;
}
