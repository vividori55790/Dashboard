using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;

namespace TelemetryDashboard.Infrastructure.Replay;

/// <summary>
/// Generates a Markdown incident post-mortem from a span of recorded DVR frames.
/// </summary>
/// <remarks>
/// Only frames an analyzer examined can appear in a conclusion. Every frame used to be read for its
/// <c>ZScore</c> and <c>IsAnomaly</c> regardless of whether anything had ever scored it, so an
/// unexamined frame arrived at the aggregates as a calm 0.0σ: it diluted "max sigma", it competed
/// for "worst channel", and it counted toward a total the report presented as analysed. Unevaluated
/// frames are now excluded from every derived figure and their number is stated, because a report
/// that quietly drops part of its input misleads in exactly the way the exclusion exists to prevent.
/// </remarks>
public class IncidentReportGenerator
{
    /// <summary>Sigma at or above which an examined frame is worth listing even if unflagged.</summary>
    private const double WarningSigma = 2.5;

    /// <summary>Rows the timeline table shows before summarising the remainder.</summary>
    private const int TimelineRowLimit = IncidentReportSections.TimelineRowLimit;

    /// <summary>Channels the per-channel breakdown shows before summarising the remainder.</summary>
    private const int ChannelRowLimit = IncidentReportSections.ChannelRowLimit;

    /// <summary>Renders the report. <paramref name="aiDiagnosisSummary"/> may be empty.</summary>
    public string GenerateMarkdownReport(string incidentTitle, IEnumerable<DvrFrame> anomalyFrames, string aiDiagnosisSummary)
    {
        var framesList = anomalyFrames.ToList();
        var unevaluated = framesList.Where(f => !f.HasVerdict).ToList();
        var criticalFrames = framesList
            .Where(f => f.HasVerdict && (f.IsAnomaly || f.ZScore >= WarningSigma))
            .ToList();
        int evaluated = framesList.Count - unevaluated.Count;

        var sb = new StringBuilder();
        sb.AppendLine($"# 🚨 Telemetry Incident Report — {incidentTitle}");
        sb.AppendLine($"> **생성 일시**: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC  ");
        sb.AppendLine($"> **분석 대상 패킷**: 총 {framesList.Count}건 (판정 완료: {evaluated}건 / 판정 없음: {unevaluated.Count}건)  ");
        sb.AppendLine($"> **이상치 포착**: {criticalFrames.Count}건  ");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 📊 1. 사건 개요 & AI 자동 진단 요약");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(aiDiagnosisSummary)
            ? "이 보고서에는 진단 요약이 제공되지 않았습니다. 아래 내용은 기록된 프레임에서 직접 도출한 관측치입니다."
            : aiDiagnosisSummary);
        sb.AppendLine();
        IncidentReportSections.AppendUnevaluatedNote(sb, unevaluated);
        sb.AppendLine("## 🔍 2. 비정상 지표 상세 타임라인 (Critical Anomalies)");
        sb.AppendLine();
        sb.AppendLine("| 타임스탬프 | 채널명 | 측정값 | Z-Score (σ) | 이상 여부 |");
        sb.AppendLine("|------------|--------|--------|-------------|-----------|");

        if (criticalFrames.Count == 0)
        {
            sb.AppendLine("| - | - | - | - | 정상 |");
        }
        else
        {
            foreach (var frame in criticalFrames.Take(TimelineRowLimit))
            {
                sb.AppendLine($"| {frame.TimestampSec:F2}s | `{frame.ChannelName}` | **{frame.Value:F2}** | `{frame.FormatZScore("F2")}` | {(frame.IsAnomaly ? "🚨 CRITICAL" : "⚠️ WARNING")} |");
            }

            IncidentReportSections.AppendTruncationNote(sb, criticalFrames.Count, TimelineRowLimit, "프레임");
        }

        sb.AppendLine();
        sb.AppendLine("## 📈 3. 데이터 기반 관측 결과");
        sb.AppendLine();
        IncidentReportSections.AppendObservations(sb, criticalFrames, evaluated, framesList.Count);
        sb.AppendLine();
        sb.AppendLine("## 🛡️ 4. 일반 점검 체크리스트");
        sb.AppendLine();
        sb.AppendLine("> 아래 항목은 이번 데이터에서 도출한 결론이 아니라 표준 점검 절차입니다.");
        sb.AppendLine();
        sb.AppendLine("1. **채널 신호 케이블 & 물리 접속부 점검**: 비정상적인 전압/온도 Z-Score 스파이크는 엣지 장치 접지 불량이나 노이즈 유입 시 발생할 수 있습니다.");
        sb.AppendLine("2. **서킷 브레이커 & 자동 절연 정책 확인**: 폭주 패킷 발생 시 메인 UI 락업 방지를 위한 서킷 브레이커가 정상 동작하였는지 확인하십시오.");
        sb.AppendLine("3. **펌웨어 OTA 재전송 권장**: 엣지 디바이스 클록 오차 오작동 시 OTA 펌웨어 플래셔를 이용하여 최신 빌드 `.bin` 패키지를 원격 송출하십시오.");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*보고서 생성기: TelemetryDashboard IncidentReportGenerator v2.0*");

        return sb.ToString();
    }
    /// <summary>Writes a rendered report to disk, returning the full path it was written to.</summary>
    public string SaveReportToFile(string markdownReportContent, string targetDirectory, string filename = "")
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            filename = $"IncidentReport_{DateTime.UtcNow:yyyyMMdd_HHmmss}.md";
        }
        Directory.CreateDirectory(targetDirectory);
        string filePath = Path.Combine(targetDirectory, filename);
        File.WriteAllText(filePath, markdownReportContent, TelemetryDashboard.Core.Services.Utf8Files.WithoutBom);
        return filePath;
    }
}
