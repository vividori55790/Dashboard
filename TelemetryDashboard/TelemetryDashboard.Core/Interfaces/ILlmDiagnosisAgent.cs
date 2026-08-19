using System;
using System.Collections.Generic;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Analytics;

namespace TelemetryDashboard.Core.Interfaces;

/// <summary>
/// Interface for LLM Natural Language Diagnosis Agent.
/// Ingests natural language queries (English & Korean) and correlates multi-channel telemetry anomalies
/// and regression slopes to produce structured root-cause diagnosis reports.
/// </summary>
public interface ILlmDiagnosisAgent
{
    event EventHandler<string>? EmergencyCommandTriggered;

    TelemetryDashboard.Core.Models.DiagnosisReport ProcessNaturalLanguageQuery(string query, IEnumerable<AnomalyResult> recentAnomalies);
    TelemetryDashboard.Core.Models.DiagnosisReport ProcessNaturalLanguageQuery(string query, IEnumerable<AnomalyResult> recentAnomalies, IDictionary<string, IEnumerable<double>>? channelRawSeries);

    void RegisterEmergencyRule(EmergencyRule rule);
    bool EvaluateEmergencyTriggers(string channelName, double zScore, out string txCommand);
}
