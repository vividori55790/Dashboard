using System.Globalization;
using System.Threading;

namespace TelemetryDashboard.Core.Analytics.Detectors;

/// <summary>
/// What one detector actually did during a run: how often it was asked, how often it answered, and
/// how often it declined.
/// </summary>
/// <remarks>
/// Exists for the same reason <c>RecordPipeline</c> counts a stage's refusals. A detector that was
/// asked forty thousand times and judged nothing looks, from the chart, exactly like a detector that
/// judged everything and found nothing wrong — and the first one is a misconfiguration an operator
/// needs to know about at the end of the run, not infer from an empty alert list.
/// </remarks>
public sealed class DetectorTally
{
    private long _offered;
    private long _judged;
    private long _withheld;
    private long _flagged;

    public DetectorTally(string detectorId) => DetectorId = detectorId;

    /// <summary>The detector these counts belong to.</summary>
    public string DetectorId { get; }

    /// <summary>Samples on a channel this detector handles.</summary>
    public long Offered => Interlocked.Read(ref _offered);

    /// <summary>Samples it actually reached a verdict on.</summary>
    public long Judged => Interlocked.Read(ref _judged);

    /// <summary>Samples it declined to judge, with a stated reason each time.</summary>
    public long Withheld => Interlocked.Read(ref _withheld);

    /// <summary>Verdicts that flagged an anomaly.</summary>
    public long Flagged => Interlocked.Read(ref _flagged);

    /// <summary>The most recent reason a verdict was withheld, or null when none has been.</summary>
    /// <remarks>
    /// One string rather than a log: the question this answers is "why is this detector silent",
    /// and the latest reason answers it. Keeping every reason would be a second unbounded store.
    /// </remarks>
    public string? LastWithheldReason { get; private set; }

    internal void Count(DetectorVerdict verdict)
    {
        Interlocked.Increment(ref _offered);

        if (verdict.HasVerdict)
        {
            Interlocked.Increment(ref _judged);
            if (verdict.IsAnomaly) Interlocked.Increment(ref _flagged);
            return;
        }

        Interlocked.Increment(ref _withheld);
        LastWithheldReason = verdict.Reason;
    }

    /// <summary>One line for the shutdown report.</summary>
    public string Summary()
    {
        string line = string.Create(CultureInfo.InvariantCulture,
            $"{DetectorId}: {Judged} judged, {Flagged} flagged, {Withheld} withheld of {Offered} offered");

        return Judged == 0 && Withheld > 0
            ? line + $" (never judged anything: {LastWithheldReason})"
            : line;
    }
}
