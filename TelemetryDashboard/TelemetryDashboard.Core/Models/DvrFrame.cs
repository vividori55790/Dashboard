namespace TelemetryDashboard.Core.Models;

/// <summary>
/// One recorded sample on the DVR timeline, together with the anomaly verdict — if any — that an
/// analyzer reached at the moment it was recorded.
/// </summary>
/// <remarks>
/// The observation and the verdict are deliberately separated. <see cref="Value"/> is what a
/// sensor reported; <see cref="ZScore"/> and <see cref="IsAnomaly"/> are what some analyzer
/// concluded, using a threshold and a window that may since have changed.
///
/// Before <see cref="AnalyzerId"/> existed there was no way to tell a recorded verdict of 0.0
/// apart from a frame no analyzer ever examined, because both left <see cref="ZScore"/> at its
/// default. Replaying history then presented "not evaluated" as "evaluated and normal". For a
/// system whose whole claim is that displayed numbers correspond to reality, silently promoting an
/// absent judgement to a confident one is the worst available failure.
/// </remarks>
public class DvrFrame
{
    // ---- Observation: what was measured ----

    /// <summary>Position on the recording timeline, in seconds.</summary>
    public double TimestampSec { get; set; }

    public string ChannelName { get; set; } = string.Empty;

    /// <summary>The measured value.</summary>
    public double Value { get; set; }

    // ---- Verdict: what an analyzer concluded about the observation ----

    /// <summary>
    /// Standard deviations from the analyzer's baseline. Meaningful only when
    /// <see cref="HasVerdict"/> is true.
    /// </summary>
    public double ZScore { get; set; }

    /// <summary>
    /// Whether the analyzer flagged this sample. Meaningful only when <see cref="HasVerdict"/>
    /// is true.
    /// </summary>
    public bool IsAnomaly { get; set; }

    /// <summary>
    /// Identifies the analyzer that produced the verdict, including the settings that determine
    /// it, or <c>null</c> when the frame was recorded without analysis.
    /// </summary>
    /// <remarks>
    /// The settings belong in the identifier because they change the answer: the same samples
    /// scored at a 2.5 sigma threshold and at 3.5 produce different verdicts, and a recording that
    /// spans a configuration change would otherwise look self-contradictory.
    /// </remarks>
    public string? AnalyzerId { get; set; }

    /// <summary>
    /// Marker for a verdict restored from a recording that did not name its analyzer — a value was
    /// stored, but its origin is unrecoverable.
    /// </summary>
    public const string UnidentifiedAnalyzer = "unidentified";

    /// <summary>
    /// True when <see cref="ZScore"/> and <see cref="IsAnomaly"/> carry a recorded judgement
    /// rather than unset defaults.
    /// </summary>
    public bool HasVerdict => !string.IsNullOrEmpty(AnalyzerId);

    /// <summary>
    /// True when a verdict exists and names the analyzer that produced it, so the reading can be
    /// reproduced or re-scored.
    /// </summary>
    public bool HasTraceableVerdict =>
        HasVerdict && !string.Equals(AnalyzerId, UnidentifiedAnalyzer, System.StringComparison.Ordinal);

    /// <summary>
    /// The z-score formatted for display, or <c>"—"</c> when no analyzer examined this frame.
    /// </summary>
    /// <remarks>
    /// Lives on the model so every surface — the DVR dialog, incident reports, the web console —
    /// renders an unevaluated frame the same way instead of each printing a convincing "0.0σ".
    /// </remarks>
    public string FormatZScore(string format = "F1") =>
        HasVerdict ? ZScore.ToString(format, System.Globalization.CultureInfo.InvariantCulture) + "σ" : "—";
}
