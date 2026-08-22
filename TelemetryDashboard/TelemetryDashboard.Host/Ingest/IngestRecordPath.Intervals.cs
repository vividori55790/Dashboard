using System;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Records;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// The optional half of the record path: deriving how long each channel has been quiet.
/// </summary>
/// <remarks>
/// Kept apart from the path itself because it is a choice, not a stage of ingest. Off by default,
/// and the account of why it exists at all -- a dead sensor being indistinguishable from a steady
/// one -- lives on <see cref="ChannelIntervalProjection"/>.
/// </remarks>
public sealed partial class IngestRecordPath
{
    /// <summary>The interval projection, or null when it was not asked for.</summary>
    public ChannelIntervalProjection? Intervals { get; }

    /// <summary>
    /// Publishes a growing interval for any channel that has gone silent, until cancelled.
    /// </summary>
    /// <remarks>
    /// Runs beside the source rather than inside it, and that is the whole point: a source that has
    /// stopped delivering is exactly the condition being watched for, so a sweep driven by the
    /// source's own loop would fall silent at the moment it was needed. It outlives the pump's read
    /// loop and ends only when the host does.
    /// </remarks>
    public async Task SweepIntervalsAsync(CancellationToken cancellationToken)
    {
        if (Intervals is null) return;

        using var ticker = new PeriodicTimer(ChannelIntervalProjection.SweepInterval);
        try
        {
            while (await ticker.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (DataRecord overdue in Intervals.Sweep(DateTimeOffset.UtcNow, LastSource))
                {
                    await _pipeline.DispatchAsync(overdue, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown. Nothing to unwind: the projection holds only timestamps.
        }
    }

    /// <summary>Port the most recent record arrived on, so a swept record keeps its attribution.</summary>
    private string LastSource { get; set; } = string.Empty;
}
