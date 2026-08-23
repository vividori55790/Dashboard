using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Host.Ingest;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Everything this run does on its own, beside serving.
/// </summary>
/// <remarks>
/// One place because the list keeps growing and each addition was a line in the entry point that
/// had to be composed correctly with the last. Two of these already outlive the thing they look
/// like they belong to: the silence sweep must keep running after a source stops delivering, since
/// that is the condition it exists to notice, and the retention sweep must run whether or not a
/// source is attached at all.
/// </remarks>
public static class BackgroundWork
{
    /// <summary>Starts every background loop and completes when all of them have stopped.</summary>
    public static Task RunAsync(
        TelemetryIngestPump? pump, ArchiveSink? archive, HostOptions? options,
        CancellationToken cancellationToken) =>
        Task.WhenAll(
            pump?.RunAllAsync(cancellationToken) ?? Task.CompletedTask,
            RetentionSweep.RunAsync(archive, cancellationToken),
            CoverageStateSweep.RunAsync(options, pump, cancellationToken));
}
