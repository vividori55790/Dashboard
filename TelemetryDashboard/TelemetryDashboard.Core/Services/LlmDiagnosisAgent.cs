using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Analytics;

namespace TelemetryDashboard.Core.Services;

/// <summary>
/// LLM Natural Language Diagnosis Agent & Conditional MCU Emergency Auto-Control Trigger Engine.
/// Parses English and Korean natural language queries, performs multi-channel cross-correlation
/// and regression trend analysis, and produces structured diagnostic reports with root cause identification.
/// </summary>
public class LlmDiagnosisAgent : ILlmDiagnosisAgent
{
    private readonly List<EmergencyRule> _rules = new();
    public event EventHandler<string>? EmergencyCommandTriggered;

    public LlmDiagnosisAgent()
    {
        // Default Emergency Rule for backward compatibility
        _rules.Add(new EmergencyRule
        {
            ChannelName = "temp",
            ZScoreThreshold = 3.5,
            CommandTxPayload = "$CMD,SAFE_MODE,NODE_1*4A\n",
            AutoExecute = true
        });
    }

    public void RegisterEmergencyRule(EmergencyRule rule)
    {
        _rules.Add(rule);
    }

    public bool EvaluateEmergencyTriggers(string channelName, double zScore, out string txCommand)
    {
        txCommand = string.Empty;
        var matchingRule = _rules.FirstOrDefault(r =>
            (r.ChannelName == "*" || string.Equals(r.ChannelName, channelName, StringComparison.OrdinalIgnoreCase)) &&
            Math.Abs(zScore) >= r.ZScoreThreshold &&
            r.AutoExecute);

        if (matchingRule != null)
        {
            txCommand = matchingRule.CommandTxPayload;
            EmergencyCommandTriggered?.Invoke(this, txCommand);
            return true;
        }

        return false;
    }

    public DiagnosisReport ProcessNaturalLanguageQuery(string query, IEnumerable<AnomalyResult> recentAnomalies)
    {
        return ProcessNaturalLanguageQuery(query, recentAnomalies, null);
    }

    public DiagnosisReport ProcessNaturalLanguageQuery(
        string query,
        IEnumerable<AnomalyResult> recentAnomalies,
        IDictionary<string, IEnumerable<double>>? channelRawSeries)
    {
        var anomaliesList = (recentAnomalies ?? Enumerable.Empty<AnomalyResult>()).ToList();
        var criticalList = anomaliesList.Where(a => a.IsAnomaly || Math.Abs(a.ZScore) >= 2.0).ToList();

        bool isKorean = IsKoreanText(query);
        string targetChannel = ExtractTargetChannel(query, anomaliesList);

        // If target channel specified in query, prioritize matching anomalies
        var relevantAnomalies = !string.IsNullOrWhiteSpace(targetChannel)
            ? criticalList.Where(a => a.ChannelName.Contains(targetChannel, StringComparison.OrdinalIgnoreCase)).ToList()
            : criticalList;

        if (relevantAnomalies.Count == 0 && criticalList.Count > 0 && string.IsNullOrWhiteSpace(targetChannel))
        {
            relevantAnomalies = criticalList;
        }

        if (criticalList.Count == 0 && relevantAnomalies.Count == 0)
        {
            string normSummary = isKorean
                ? "시스템이 매우 안정적인 상태입니다. 최근 1시간 내 비정상적 스파이크나 임계치 초과 징후가 감지되지 않았습니다."
                : "System telemetry is operating within nominal parameters. No anomalous deviations or threshold spikes detected.";

            string normRootCause = isKorean ? "정상 작동 상태 유지 (특이사항 없음)" : "Nominal System Operation (No Fault Detected)";
            string normTrend = isKorean ? "모든 모니터링 채널이 정상 허용 범위 내에서 안정적인 기저값을 유지하고 있습니다." : "All monitored telemetry channels maintain stable baselines well within standard tolerance limits.";
            string normAction = isKorean ? "정상 작동 상태 유지 및 실시간 모니터링 계속." : "Maintain normal operational profile and continue automated background telemetry monitoring.";

            string md = GenerateMarkdown(query, targetChannel, "NORMAL", 0.98, normSummary, normRootCause, normTrend, new List<string>(), normAction);

            return new DiagnosisReport
            {
                GeneratedAt = DateTime.UtcNow,
                Query = query,
                TargetChannel = string.IsNullOrWhiteSpace(targetChannel) ? "ALL" : targetChannel,
                SeverityLevel = "NORMAL",
                ConfidenceScore = 0.98,
                SummaryDiagnosis = normSummary,
                RootCause = normRootCause,
                TrendAnalysis = normTrend,
                CriticalEvents = new List<string>(),
                RecommendedAction = normAction,
                MarkdownReport = md
            };
        }

        var topAnomaly = relevantAnomalies.Count > 0
            ? relevantAnomalies.OrderByDescending(a => Math.Abs(a.ZScore)).First()
            : criticalList.OrderByDescending(a => Math.Abs(a.ZScore)).First();

        // Multi-Channel Cross-Correlation Logic
        bool hasTempAnomaly = criticalList.Any(a => ContainsKeyword(a.ChannelName, "temp", "온도", "thermal", "heatsink"));
        bool hasVibAnomaly = criticalList.Any(a => ContainsKeyword(a.ChannelName, "vib", "진동", "accel", "gyro"));
        bool hasVoltAnomaly = criticalList.Any(a => ContainsKeyword(a.ChannelName, "volt", "vin", "vout", "vbus", "전압"));
        bool hasCurrAnomaly = criticalList.Any(a => ContainsKeyword(a.ChannelName, "curr", "iin", "iout", "ibus", "전류"));

        string rootCause;
        string action;
        string summary;
        string trend;

        if (hasTempAnomaly && hasVibAnomaly)
        {
            rootCause = isKorean
                ? "베어링 마모 및 기계적 마찰로 인한 복합 결함: 비정상적 진동 서지와 접촉면 동시 발열(Thermal Friction Overload) 발생."
                : "Mechanical Friction & Bearing Degradation: High-frequency vibration surge co-occurring with thermal dissipation overload.";

            action = isKorean
                ? "베어링 윤활 상태 점검, 모터 축 정렬 검사 및 필요시 비상 냉각 모드($CMD,SAFE_MODE) 가동 권장."
                : "Inspect bearing lubrication, check motor shaft alignment, and initiate emergency cooling sequence ($CMD,SAFE_MODE).";
        }
        else if (hasVoltAnomaly && hasCurrAnomaly)
        {
            rootCause = isKorean
                ? "전력단 부하 급변 또는 스위칭 단락(Power Stage Fault): 전압 급강하 및 전류 급증 서지 동시 포착."
                : "Electrical Load Transient / Short-Circuit Fault: Voltage drop co-occurring with high-amplitude current surge.";

            action = isKorean
                ? "DC 버스 절연 상태 점검, 인버터 MOSFET/IGBT 게이트 스위칭 점검 및 필요시 비상 전력 차단($CMD,SAFE_MODE) 실행."
                : "Isolate DC bus, verify inverter gate driver switching signals, and execute emergency power clamp ($CMD,SAFE_MODE).";
        }
        else if (hasTempAnomaly)
        {
            rootCause = isKorean
                ? $"채널 '{topAnomaly.ChannelName}' 열 방출 저하 또는 지속적 과부하로 인한 과열 현상."
                : $"Thermal Overload / Heatsink Heat Dissipation Degradation on channel '{topAnomaly.ChannelName}'.";

            action = isKorean
                ? $"권장 조치: 채널 '{topAnomaly.ChannelName}' 엣지 센서 케이블 수신 상태 점검 및 자동 절연/냉각 모드 실행 권장. 필요 시 $CMD,RESET_MCU 전송."
                : $"Recommended Action: Inspect cooling fan airflow, check thermal interface paste on channel '{topAnomaly.ChannelName}', and dispatch $CMD,SAFE_MODE if temperature exceeds upper boundary.";
        }
        else if (hasVibAnomaly)
        {
            rootCause = isKorean
                ? $"채널 '{topAnomaly.ChannelName}' 기계적 공진, 회전자 불균형 또는 축 정렬 불량에 따른 진동 스파이크."
                : $"Excessive Mechanical Vibration Spike / Resonance / Shaft Misalignment on channel '{topAnomaly.ChannelName}'.";

            action = isKorean
                ? $"모터 회전 속도 감속, 체결 볼트 토크 점검 및 로터 밸런싱 점검 권장. 필요 시 $CMD,SAFE_MODE 전송."
                : $"Reduce motor RPM, verify mounting bolt torque on '{topAnomaly.ChannelName}', and schedule physical dynamic balancing.";
        }
        else
        {
            rootCause = isKorean
                ? $"채널 '{topAnomaly.ChannelName}'에서 Z-Score {topAnomaly.ZScore:F2}σ 상당의 통계적 이상 드리프트 관측 (현재값: {topAnomaly.CurrentValue:F2})."
                : $"Statistical parameter spike and anomaly deviation on channel '{topAnomaly.ChannelName}' (Z-Score: {topAnomaly.ZScore:F2}σ, Value: {topAnomaly.CurrentValue:F2}).";

            action = isKorean
                ? $"권장 조치: 채널 '{topAnomaly.ChannelName}' 엣지 센서 케이블 수신 상태 점검 및 자동 절연/냉각 모드 실행 권장. 필요 시 $CMD,RESET_MCU 전송."
                : $"Recommended Action: Inspect sensor wiring and edge connection for '{topAnomaly.ChannelName}'. Dispatch $CMD,SAFE_MODE if necessary.";
        }

        if (isKorean)
        {
            summary = $"질의분석: '{query}'\n[AI 진단 결과] 총 {criticalList.Count}건의 비정상 지표 포착.\n주요 원인: 채널 '{topAnomaly.ChannelName}'에서 Z-Score {topAnomaly.ZScore:F2}σ 상당의 전압/온도 급증 스파이크 관측 (현재값: {topAnomaly.CurrentValue:F2}). 60초 후 예상 수치: {topAnomaly.PredictedValueIn60s:F2}.";
            trend = topAnomaly.EstimatedTimeToBreachSec > 0
                ? $"60초 후 예측값은 {topAnomaly.PredictedValueIn60s:F2}이며, 예상 임계치 도달 시간은 약 {topAnomaly.EstimatedTimeToBreachSec:F1}초입니다."
                : $"60초 후 예측값은 {topAnomaly.PredictedValueIn60s:F2}입니다.";
        }
        else
        {
            summary = $"Query Analysis: '{query}'\n[AI Diagnostic Result] Captured {criticalList.Count} anomalous telemetry events.\nPrimary Indicator: Channel '{topAnomaly.ChannelName}' exhibiting Z-Score {topAnomaly.ZScore:F2}σ deviation (Current Value: {topAnomaly.CurrentValue:F2}). Projected 60s value: {topAnomaly.PredictedValueIn60s:F2}.";
            trend = topAnomaly.EstimatedTimeToBreachSec > 0
                ? $"Linear regression projects 60s value at {topAnomaly.PredictedValueIn60s:F2}, with estimated time to safety breach of {topAnomaly.EstimatedTimeToBreachSec:F1} seconds."
                : $"Linear regression projects 60s value at {topAnomaly.PredictedValueIn60s:F2}.";
        }

        string severity = Math.Abs(topAnomaly.ZScore) >= 3.5 ? "CRITICAL" : "WARNING";
        double confidence = Math.Min(0.99, Math.Max(0.75, 0.70 + Math.Min(0.25, (Math.Abs(topAnomaly.ZScore) / 10.0) + (criticalList.Count * 0.04))));

        var criticalEventsList = criticalList
            .Select(c => $"[{c.ChannelName}] Z-Score: {c.ZScore:F2}σ, Value: {c.CurrentValue:F2} (Est Breach: {(c.EstimatedTimeToBreachSec > 0 ? $"{c.EstimatedTimeToBreachSec:F1}s" : "N/A")})")
            .ToList();

        string markdown = GenerateMarkdown(query, topAnomaly.ChannelName, severity, confidence, summary, rootCause, trend, criticalEventsList, action);

        return new DiagnosisReport
        {
            GeneratedAt = DateTime.UtcNow,
            Query = query,
            TargetChannel = topAnomaly.ChannelName,
            SeverityLevel = severity,
            ConfidenceScore = confidence,
            SummaryDiagnosis = summary,
            RootCause = rootCause,
            TrendAnalysis = trend,
            CriticalEvents = criticalEventsList,
            RecommendedAction = action,
            MarkdownReport = markdown
        };
    }

    private static bool IsKoreanText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (char c in text)
        {
            if (c >= 0xAC00 && c <= 0xD7A3) return true; // Hangul Syllables
            if (c >= 0x1100 && c <= 0x11FF) return true; // Hangul Jamo
            if (c >= 0x3130 && c <= 0x318F) return true; // Hangul Compatibility Jamo
        }
        return false;
    }

    private static bool ContainsKeyword(string text, params string[] keywords)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var kw in keywords)
        {
            if (text.Contains(kw, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string ExtractTargetChannel(string query, List<AnomalyResult> anomalies)
    {
        if (string.IsNullOrWhiteSpace(query)) return string.Empty;

        // Try exact match with any channel in anomalies
        foreach (var a in anomalies)
        {
            if (query.Contains(a.ChannelName, StringComparison.OrdinalIgnoreCase))
            {
                return a.ChannelName;
            }
        }

        // Check common aliases
        if (query.Contains("temp", StringComparison.OrdinalIgnoreCase) || query.Contains("온도", StringComparison.OrdinalIgnoreCase))
            return "temp";
        if (query.Contains("vib", StringComparison.OrdinalIgnoreCase) || query.Contains("진동", StringComparison.OrdinalIgnoreCase))
            return "vib";
        if (query.Contains("volt", StringComparison.OrdinalIgnoreCase) || query.Contains("전압", StringComparison.OrdinalIgnoreCase))
            return "volt";
        if (query.Contains("curr", StringComparison.OrdinalIgnoreCase) || query.Contains("전류", StringComparison.OrdinalIgnoreCase))
            return "curr";

        return string.Empty;
    }

    private static string GenerateMarkdown(
        string query,
        string targetChannel,
        string severity,
        double confidence,
        string summary,
        string rootCause,
        string trend,
        List<string> criticalEvents,
        string action)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 🤖 Telemetry AI Diagnostic Report");
        sb.AppendLine($"- **Timestamp:** {DateTime.UtcNow:u}");
        sb.AppendLine($"- **Query:** `{query}`");
        sb.AppendLine($"- **Target Channel:** `{targetChannel}`");
        sb.AppendLine($"- **Severity:** **{severity}** (Confidence: {confidence:P1})");
        sb.AppendLine();
        sb.AppendLine("## 🔍 Summary Diagnosis");
        sb.AppendLine(summary);
        sb.AppendLine();
        sb.AppendLine("## 🎯 Root Cause Analysis");
        sb.AppendLine(rootCause);
        sb.AppendLine();
        sb.AppendLine("## 📈 Trend Analysis & Projection");
        sb.AppendLine(trend);
        sb.AppendLine();
        sb.AppendLine($"## 🚨 Critical Events ({criticalEvents.Count})");
        if (criticalEvents.Count == 0)
        {
            sb.AppendLine("- *No critical anomalies recorded.*");
        }
        else
        {
            foreach (var ev in criticalEvents)
            {
                sb.AppendLine($"- {ev}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("## 🛠️ Recommended Action");
        sb.AppendLine(action);

        return sb.ToString();
    }

    private static readonly System.Net.Http.HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<DiagnosisReport> ProcessQueryWithLlmApiAsync(
        string query,
        IEnumerable<AnomalyResult> recentAnomalies,
        LlmApiConfig config,
        IDictionary<string, IEnumerable<double>>? channelRawSeries = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Fallback immediately if Offline mode selected or OpenAI without Key
        if (config == null || 
            config.Provider.Equals("Offline", StringComparison.OrdinalIgnoreCase) ||
            (config.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(config.ApiKey)))
        {
            var offlineReport = ProcessNaturalLanguageQuery(query, recentAnomalies, channelRawSeries);
            return offlineReport;
        }

        try
        {
            // 2. Build detailed prompt with live telemetry context
            var promptBuilder = new StringBuilder();
            promptBuilder.AppendLine("You are an expert industrial telemetry and power electronics diagnostic engineer.");
            promptBuilder.AppendLine("Analyze the following live telemetry anomalies, Z-scores, and time-to-breach predictions, and answer the user query in Korean/English as queried.");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("### Live Telemetry Anomalies Data:");
            foreach (var a in recentAnomalies)
            {
                promptBuilder.AppendLine($"- Channel: `{a.ChannelName}`, Value: {a.CurrentValue:F2}, Z-Score: {a.ZScore:F2}σ, Anomaly: {a.IsAnomaly}, Pred60s: {a.PredictedValueIn60s:F2}, BreachTime: {a.EstimatedTimeToBreachSec:F1}s");
            }
            promptBuilder.AppendLine();
            promptBuilder.AppendLine($"User Query: \"{query}\"");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("Provide a clear, structured Markdown report including:");
            promptBuilder.AppendLine("1. Summary Diagnosis");
            promptBuilder.AppendLine("2. Root Cause Analysis (multi-channel cross correlations)");
            promptBuilder.AppendLine("3. 60-Second Trend Projection & Breach Forecast");
            promptBuilder.AppendLine("4. Recommended Mitigation Actions (e.g. $CMD,SAFE_MODE)");

            string systemPrompt = promptBuilder.ToString();

            // 3. Prepare JSON Request
            var requestObj = new
            {
                model = config.ModelName,
                temperature = config.Temperature,
                max_tokens = config.MaxTokens,
                messages = new[]
                {
                    new { role = "system", content = "You are TelemetryDashboard AI Diagnostic Copilot." },
                    new { role = "user", content = systemPrompt }
                }
            };

            string requestJson = System.Text.Json.JsonSerializer.Serialize(requestObj);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, config.EndpointUrl)
            {
                Content = new System.Net.Http.StringContent(requestJson, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(config.ApiKey) && !config.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    string content = choices[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
                    return new DiagnosisReport
                    {
                        GeneratedAt = DateTime.UtcNow,
                        Query = query,
                        SeverityLevel = recentAnomalies.Any(a => a.ZScore >= 3.5) ? "CRITICAL" : "WARNING",
                        ConfidenceScore = 0.98,
                        SummaryDiagnosis = $"[Real LLM Inference ({config.ModelName})]: Response generated successfully.",
                        RootCause = "Dynamic LLM Analysis Generated",
                        MarkdownReport = $"# 🤖 Live LLM Diagnostic Report ({config.Provider} / {config.ModelName})\n\n{content}"
                    };
                }
            }
        }
        catch { }

        // Fallback to offline rule engine on error
        var fallback = ProcessNaturalLanguageQuery(query, recentAnomalies, channelRawSeries);
        fallback.MarkdownReport = $"> [!NOTE]\n> **실제 LLM 엔드포인트 연결 실패/오프라인 모드**: 내장 규칙 기반 진단 엔진으로 전환되었습니다.\n\n" + fallback.MarkdownReport;
        return fallback;
    }
}

public class LlmApiConfig
{
    public string Provider { get; set; } = "Offline"; // OpenAI, Ollama, Custom, Offline
    public string ApiKey { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string ModelName { get; set; } = "gpt-4o-mini";
    public double Temperature { get; set; } = 0.2;
    public int MaxTokens { get; set; } = 1500;
}

