using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Host.Ingest;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Writing the remembered fleet while the hub is running, not only when it stops.
/// </summary>
/// <remarks>
/// Saving at shutdown alone was measured and it does not work: a host stopped with anything other
/// than Ctrl-C — killed, crashed, or a machine that lost power — never reaches the shutdown path,
/// so the file stays as it was and the fleet is forgotten. Those are the runs where the memory
/// matters most: nobody restarts a healthy hub to find out what is missing.
/// <para>
/// The write is skipped unless the set actually changed, so a steady fleet costs one comparison a
/// tick and touches no disk. A node is learned once and then never again, which makes changes rare
/// by nature.
/// </para>
/// </remarks>
public static class CoverageStateSweep
{
    /// <summary>How often the remembered set is compared against the file.</summary>
    /// <remarks>
    /// Thirty seconds, matching the ledger's own silence threshold: a node that has been quiet long
    /// enough to be called missing has already been in the remembered set for at least that long.
    /// Losing less than one interval of learning to a hard kill is the bound this buys.
    /// </remarks>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <summary>Saves whenever the fleet changes, until cancelled.</summary>
    public static async Task RunAsync(
        HostOptions? options, TelemetryIngestPump? pump, CancellationToken cancellationToken)
    {
        if (options?.CoverageStatePath is not { } path || pump is null) return;

        // Seeded from what is already known, which after CoverageSetup.Apply is the restored file
        // plus anything declared -- so a start that learns nothing writes nothing.
        var written = new HashSet<string>(pump.Coverage.KnownNodes, StringComparer.OrdinalIgnoreCase);

        using var ticker = new PeriodicTimer(Interval);
        try
        {
            while (await ticker.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                IReadOnlyList<string> known = pump.Coverage.KnownNodes;
                if (known.Count == written.Count && known.All(written.Contains)) continue;

                if (CoverageStateFile.Write(path, pump.Coverage.KnownNodeHistory) is { } failure)
                {
                    Console.Error.WriteLine($"telemetry-host: {failure}");
                    return;
                }

                written = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown. CoverageSetup.Save writes the final set after the drain.
        }
    }
}
