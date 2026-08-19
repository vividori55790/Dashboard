using System.Collections.Generic;
using System.Globalization;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.UI.Dialogs;

/// <summary>One line of the time-travel DVR snapshot grid.</summary>
/// <remarks>
/// Every column is a string, including <see cref="Value"/>. It was a <see cref="double"/> while the
/// grid still had to render a row for a moment at which nothing was recorded, and the type's default
/// then displayed as a reading the hardware never produced. A column with nothing to show must be
/// able to say so.
/// </remarks>
public sealed class ReplayRowItem
{
    /// <summary>Channel the row describes, or <see cref="ReplaySnapshotRows.NoData"/>.</summary>
    public string ChannelName { get; set; } = string.Empty;

    /// <summary>Measured value, formatted, or <see cref="ReplaySnapshotRows.NoData"/>.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Formatted z-score, or an em dash when no analyzer examined the frame.</summary>
    public string ZScore { get; set; } = string.Empty;

    /// <summary>Verdict text, or the reason no verdict is shown.</summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>Maps DVR frames onto <see cref="ReplayRowItem"/> rows for the snapshot grid.</summary>
/// <remarks>
/// Split out of the dialog because the empty case is the one that matters. An empty snapshot used to
/// be filled with three invented channels reporting comfortable values at low sigma, so a window in
/// which the hardware said nothing at all was presented to an operator as a window in which
/// everything was fine. Nothing here originates a number: a row exists only for a recorded frame,
/// and the single row that stands in for an empty window states that it is empty.
/// </remarks>
public static class ReplaySnapshotRows
{
    /// <summary>Placeholder for a column that has nothing to display.</summary>
    public const string NoData = "—";

    private const string EmptyWindowStatus =
        "이 구간에 기록된 프레임이 없습니다 (no frames recorded in this window)";

    private const string NoVerdictStatus = "— 판정 없음 (not evaluated)";

    private const string AnomalyStatus = "⚠️ CRITICAL";

    private const string NormalStatus = "✅ NORMAL";

    /// <summary>
    /// Builds the grid rows for a snapshot, returning a single explanatory row when it is empty.
    /// </summary>
    public static List<ReplayRowItem> Build(IReadOnlyList<DvrFrame>? frames)
    {
        var rows = new List<ReplayRowItem>();

        if (frames is null || frames.Count == 0)
        {
            rows.Add(new ReplayRowItem
            {
                ChannelName = NoData,
                Value = NoData,
                ZScore = NoData,
                Status = EmptyWindowStatus
            });
            return rows;
        }

        foreach (DvrFrame frame in frames)
        {
            rows.Add(new ReplayRowItem
            {
                ChannelName = frame.ChannelName,
                Value = frame.Value.ToString("F2", CultureInfo.InvariantCulture),
                ZScore = frame.FormatZScore(),
                Status = DescribeVerdict(frame)
            });
        }

        return rows;
    }

    /// <summary>
    /// Renders the verdict column.
    /// </summary>
    /// <remarks>
    /// A frame nobody scored is neither normal nor critical, and the previous two-way branch on
    /// <c>IsAnomaly</c> resolved that absence to a green tick — the same fabrication as a confident
    /// "0.0σ", in the column an operator actually scans.
    /// </remarks>
    private static string DescribeVerdict(DvrFrame frame)
    {
        if (!frame.HasVerdict) return NoVerdictStatus;
        return frame.IsAnomaly ? AnomalyStatus : NormalStatus;
    }
}
