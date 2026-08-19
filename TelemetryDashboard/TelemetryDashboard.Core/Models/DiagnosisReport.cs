using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Models;

/// <summary>
/// Structured AI Telemetry Diagnosis Report model.
/// Contains root cause analysis, multi-channel cross-correlations, severity classification,
/// confidence scoring, time-to-breach trend projections, and markdown report representation.
/// </summary>
public class DiagnosisReport
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string Query { get; set; } = string.Empty;
    public string TargetChannel { get; set; } = string.Empty;
    public string SeverityLevel { get; set; } = "NORMAL"; // NORMAL, WARNING, CRITICAL
    public double ConfidenceScore { get; set; } = 0.95;
    public string SummaryDiagnosis { get; set; } = string.Empty;
    public string RootCause { get; set; } = string.Empty;
    public string TrendAnalysis { get; set; } = string.Empty;
    public List<string> CriticalEvents { get; set; } = new();
    public string RecommendedAction { get; set; } = string.Empty;
    public string MarkdownReport { get; set; } = string.Empty;
}
