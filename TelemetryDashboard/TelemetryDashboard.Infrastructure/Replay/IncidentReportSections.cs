using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Infrastructure.Replay;

/// <summary>
/// Markdown section builders for <see cref="IncidentReportGenerator"/>.
/// </summary>
/// <remarks>
/// Split out so the generator file stays within the repository's 150-line rule, but the seam is
/// real: everything here turns an already-filtered frame set into prose, and none of it decides
/// what belongs in the report. That decision — which frames carry a verdict — lives in the
/// generator alone, so there is exactly one place where an unexamined frame can be let in.
/// </remarks>
internal static class IncidentReportSections
{
    /// <summary>Rows the timeline table shows before summarising the remainder.</summary>
    internal const int TimelineRowLimit = 20;

    /// <summary>Channels the per-channel breakdown shows before summarising the remainder.</summary>
    internal const int ChannelRowLimit = 10;

    /// <summary>
    /// States how many frames carried no verdict, and which channels they came from.
    /// </summary>
    /// <remarks>
    /// Naming the channels matters more than the count: a channel that appears here and nowhere
    /// else was recorded but never analysed, which reads as "quiet" in every other section of the
    /// report. Silence about the exclusion would be the same failure as scoring the frames at zero.
    /// </remarks>
    internal static void AppendUnevaluatedNote(StringBuilder sb, List<DvrFrame> unevaluated)
    {
        if (unevaluated.Count == 0) return;

        string[] channels = unevaluated
            .Select(f => f.ChannelName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        sb.AppendLine($"> ⚠️ **판정 없는 프레임 {unevaluated.Count}건이 아래 모든 집계에서 제외되었습니다.** ");
        sb.AppendLine("> 이 프레임들은 분석기가 검사한 적이 없으므로 Z-Score 0.0은 \"정상\"이 아니라 \"판정 없음\"입니다.  ");
        sb.AppendLine($"> 해당 채널({channels.Length}개): {string.Join(", ", channels.Take(ChannelRowLimit).Select(c => $"`{c}`"))}"
            + (channels.Length > ChannelRowLimit ? $" 외 {channels.Length - ChannelRowLimit}개" : string.Empty));
        sb.AppendLine();
    }

    /// <summary>Records that a table was truncated, so a shortened list never reads as a complete one.</summary>
    internal static void AppendTruncationNote(StringBuilder sb, int total, int shown, string unit)
    {
        if (total <= shown) return;
        sb.AppendLine($"> 위 표에는 {total}건 중 {shown}건만 표시했습니다 (나머지 {total - shown}{unit} 생략).");
    }

    /// <summary>
    /// Derives observations from the captured frames.
    /// The section that follows is a fixed checklist; keeping the two apart stops a generic
    /// procedure from reading as a conclusion drawn from this incident's data.
    /// </summary>
    /// <remarks>
    /// <paramref name="criticalFrames"/> has already been filtered to frames carrying a verdict, so
    /// the maxima and orderings below compare judgements against judgements. Ranking an unexamined
    /// frame's default 0.0 alongside real scores is how "worst channel" used to be decided.
    /// </remarks>
    /// <param name="evaluated">Frames carrying a verdict; an all-clear can only be claimed over these.</param>
    /// <param name="total">Frames supplied, including those no analyzer examined.</param>
    internal static void AppendObservations(StringBuilder sb, List<DvrFrame> criticalFrames, int evaluated, int total)
    {
        if (criticalFrames.Count == 0)
        {
            // "Nothing exceeded the threshold" is a finding, and a finding needs something to have
            // been measured against it. With no verdicts in the window there is no all-clear to give.
            sb.AppendLine(evaluated > 0
                ? $"판정된 {evaluated}건 중 임계치를 초과한 채널이 없습니다."
                : total == 0
                    ? "이 구간에 기록된 프레임이 없어 판단할 근거가 없습니다."
                    : $"기록된 {total}건 중 분석기가 판정한 프레임이 없어 이 구간은 평가할 수 없습니다.");
            return;
        }

        var worst = criticalFrames.OrderByDescending(f => f.ZScore).First();
        var byChannel = criticalFrames
            .GroupBy(f => f.ChannelName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToList();

        double spanSec = criticalFrames.Max(f => f.TimestampSec) - criticalFrames.Min(f => f.TimestampSec);

        sb.AppendLine($"- **최대 편차 채널**: `{worst.ChannelName}` — {worst.Value:F3} ({worst.FormatZScore("F2")})");
        sb.AppendLine($"- **이상 발생 구간 길이**: {spanSec:F2}초");
        sb.AppendLine($"- **영향 채널 수**: {byChannel.Count}개");
        sb.AppendLine();
        sb.AppendLine("| 채널 | 이상 프레임 수 | 최대 Z-Score | 평균 측정값 |");
        sb.AppendLine("|------|----------------|--------------|-------------|");

        foreach (var group in byChannel.Take(ChannelRowLimit))
        {
            sb.AppendLine($"| `{group.Key}` | {group.Count()} | {group.Max(f => f.ZScore):F2}σ | {group.Average(f => f.Value):F3} |");
        }

        AppendTruncationNote(sb, byChannel.Count, ChannelRowLimit, "개 채널");
    }

}
