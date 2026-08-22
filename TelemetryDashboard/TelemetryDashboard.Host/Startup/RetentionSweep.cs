using System;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Storage;
using TelemetryDashboard.Host.Ingest;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Runs the archive's retention on a clock, and says what it removed.
/// </summary>
/// <remarks>
/// <c>TieredTelemetryStore</c> has had a prune path and a retention log for a long time and its own
/// remarks record the gap plainly: "Nothing calls this on a timer and nothing calls it at start-up."
/// So an archive with a policy grew exactly as fast as one without, and the tiering, the rollups
/// and the sweep were all written, all tested and all off.
/// <para>
/// Nothing is deleted unless an operator asked for it with <c>--retain</c>. What this adds is the
/// clock, and the account: every prune is printed with what went, because a deletion nobody was
/// told about is one nobody can question.
/// </para>
/// </remarks>
public static class RetentionSweep
{
    /// <summary>How often the policy is applied.</summary>
    /// <remarks>
    /// Hourly. The finest tier this policy can express is seconds, but a prune is a delete against
    /// a live database and running it every minute would spend more of the disk's time on
    /// housekeeping than on the data. An hour of slack past a cutoff costs an hour of rows.
    /// </remarks>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>
    /// Prunes once at start-up and then on the interval, until cancelled.
    /// </summary>
    /// <remarks>
    /// Once at start-up as well, because the common shape of this feature is a host restarted after
    /// being down for a week — waiting an hour to enforce a policy that is already an hour overdue
    /// serves nobody.
    /// </remarks>
    public static async Task RunAsync(ArchiveSink? archive, CancellationToken cancellationToken)
    {
        if (archive?.Tiered is not { } store || !store.Retention.Enabled) return;

        await PruneOnceAsync(store, cancellationToken).ConfigureAwait(false);

        using var ticker = new PeriodicTimer(Interval);
        try
        {
            while (await ticker.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await PruneOnceAsync(store, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown. A prune half-done is a transaction rolled back, not a corrupt store.
        }
    }

    private static async Task PruneOnceAsync(
        Infrastructure.Storage.TieredTelemetryStore store, CancellationToken cancellationToken)
    {
        try
        {
            RetentionPruneReport report = await store.PruneAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // Only when it did something. A line an hour saying nothing was removed is how an
            // operator learns to stop reading the lines.
            if (report.RemovedAnything) Console.WriteLine("  retention     " + report.Describe());
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            // The archive is still being written to; a failed prune is a disk that stays fuller
            // than asked, not a reason to end a run that is otherwise recording fine.
            Console.Error.WriteLine($"telemetry-host: retention sweep failed ({ex.GetType().Name}); "
                + "the archive keeps growing until it succeeds.");
        }
    }
}
