using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Host.Outbound;

/// <summary>
/// Writes the incident window to disk the moment a limit is crossed.
/// </summary>
/// <remarks>
/// The report this saves has existed since <c>/api/incident</c> gained per-channel verdicts, and
/// the only way to get one was for somebody to ask at the right moment with the right timestamp. An
/// alarm at three in the morning names that moment and nobody is awake to type it; by the time
/// anyone looks, the instant has to be guessed from a log line, and the run-up they actually wanted
/// is whatever the guess happened to catch.
/// <para>
/// So the trigger is the crossing itself. A limit entering breach is the one event in a run that
/// unambiguously means "capture what led to this" -- which is exactly what the baseline entry for
/// <c>NotionClient</c> said the host did not have. It does now, and this writes the report to a
/// file rather than to anyone's cloud account, because a file needs no credential, no network and
/// no third party's availability at the moment a machine is misbehaving.
/// </para>
/// <para>
/// Requires an archive. The window comes out of it, so without one there is nothing to capture and
/// the host says so at start-up rather than writing empty reports all night.
/// </para>
/// </remarks>
public sealed class IncidentCaptureRelay
{
    /// <summary>Default quiet period per rule, so a flapping limit does not fill the disk.</summary>
    /// <remarks>
    /// Per rule rather than per channel: two different limits on one channel describe two different
    /// faults, and sharing a cooldown would let the first to fire hide the second.
    /// </remarks>
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(2);

    /// <summary>Seconds of run-up captured before the crossing.</summary>
    public const double LeadSeconds = 60.0;

    /// <summary>Seconds captured after it, to show how the system responded.</summary>
    public const double TrailSeconds = 5.0;

    /// <summary>
    /// How long the capture waits after the crossing before reading the window.
    /// </summary>
    /// <remarks>
    /// Measured, not guessed at. Capturing the instant the limit was crossed produced a report
    /// containing neither the event nor its aftermath: the archive is written through a bounded
    /// channel and a drain, so at the moment of the crossing the samples that caused it were still
    /// in flight, and the five seconds of response had not happened yet. The first live capture
    /// held a 47.96..48.07 V window for a channel that had just gone to 54 V, and named nothing as
    /// anomalous.
    /// <para>
    /// So the wait is the trail plus room for the drain to catch up. It delays the report, not the
    /// alarm -- whichever relay pages someone has already done so.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan CaptureDelay = TimeSpan.FromSeconds(TrailSeconds + 4.0);

    private readonly IDataLogger _archive;
    private readonly string _directory;
    private readonly AlertThrottle _throttle;
    private long _captured;
    private long _throttled;
    private long _failed;

    private readonly TimeSpan _delay;

    /// <param name="captureDelay">
    /// How long to wait after the crossing before reading the window. Configurable so a test does
    /// not have to sit through it, and because a slower archive on a busier rig may want longer.
    /// </param>
    public IncidentCaptureRelay(
        IDataLogger archive, string directory, TimeSpan? cooldown = null, TimeSpan? captureDelay = null)
    {
        _archive = archive ?? throw new ArgumentNullException(nameof(archive));
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _throttle = new AlertThrottle(cooldown ?? DefaultCooldown);
        _delay = captureDelay ?? CaptureDelay;
    }

    /// <summary>
    /// Captures one crossing and completes when its report is on disk.
    /// </summary>
    /// <remarks>
    /// The event handler cannot await -- it runs on the thread publishing the sample and must not
    /// wait on a disk -- so the work is started and dropped there. This is the same work with a
    /// task to wait on, which is what a test needs and what nothing in the host does.
    /// </remarks>
    public Task CaptureAsync(ScoredSample sample, BreachedLimit breach) => WriteReportAsync(sample, breach);

    /// <summary>Reports written.</summary>
    public long Captured => Interlocked.Read(ref _captured);

    /// <summary>Crossings inside a rule's quiet period, so no second report was written.</summary>
    public long Throttled => Interlocked.Read(ref _throttled);

    /// <summary>Crossings whose report could not be written.</summary>
    /// <remarks>
    /// Counted rather than thrown. A full disk during an incident is a bad moment to take the host
    /// down, and the alarm itself has already gone out through whichever relay carries it.
    /// </remarks>
    public long Failed => Interlocked.Read(ref _failed);

    /// <summary>Path of the most recent report, for the shutdown line.</summary>
    public string? LastReportPath { get; private set; }

    /// <summary>Handler for the publisher's scored-sample event.</summary>
    /// <remarks>
    /// Only the crossing, never the samples that follow it. A converter held outside its band for
    /// an hour is one incident, and a report per sample would bury the one that explains it.
    /// </remarks>
    public void OnSampleScored(object? sender, ScoredSample sample)
    {
        foreach (BreachedLimit breach in sample.LimitTransitions)
        {
            if (!breach.JustEntered) continue;

            if (!_throttle.ShouldSend(breach.Rule.Declaration, out _))
            {
                Interlocked.Increment(ref _throttled);
                continue;
            }

            // Fire and forget: the ingest thread publishing this sample must not wait on a disk.
            _ = WriteReportAsync(sample, breach);
        }
    }

    private async Task WriteReportAsync(ScoredSample sample, BreachedLimit breach)
    {
        try
        {
            LastReportPath = await IncidentReportWriter
                .WriteAsync(_archive, _directory, sample, breach, _delay).ConfigureAwait(false);
            Interlocked.Increment(ref _captured);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Interlocked.Increment(ref _failed);
        }
    }
}
