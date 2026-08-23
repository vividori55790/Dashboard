using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Cluster;
using TelemetryDashboard.Core.Streaming;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Host.Ingest;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Telling the ledger what this rig is, and putting the answer somewhere it can be read.
/// </summary>
/// <remarks>
/// The ledger shipped able to answer three questions and was asked none of them. It learned nodes
/// that spoke, which is the half that worked; nothing declared the nodes that had not, nothing
/// remembered the learned set across a restart, and nothing retired a node that was removed on
/// purpose. Its own remarks named all three gaps.
/// <para>
/// The fourth was in the reporting rather than the ledger: coverage was printed once, at shutdown.
/// An operator could learn that a converter had stopped reporting an hour ago only by stopping the
/// hub as well, which is the opposite of what somebody investigating a live rig wants to do.
/// </para>
/// </remarks>
public static class CoverageSetup
{
    /// <summary>
    /// Restores, declares, retires, and publishes coverage. Returns the lines to print.
    /// </summary>
    /// <remarks>
    /// The order matters and is the whole of the decision here. Restore first, so a remembered node
    /// exists to be retired. Declare next, because <c>--expect</c> is what the operator says the rig
    /// is now. Retire last, so removing a node from the rig beats both -- otherwise a node in the
    /// state file would be resurrected on every start by the very file meant to remember it.
    /// </remarks>
    public static IReadOnlyList<string> Apply(
        HostOptions options, TelemetryIngestPump? pump, TelemetryStreamingServer server)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(server);

        if (pump is null) return [];

        CoverageLedger ledger = pump.Coverage;
        var lines = new List<string>();

        IReadOnlyList<CoverageStateEntry> remembered =
            CoverageStateFile.Read(options.CoverageStatePath, out string? note);
        if (note is not null) lines.Add("           " + note);
        foreach (CoverageStateEntry entry in remembered) ledger.Expect(entry.Node, entry.LastHeard);

        foreach (string id in options.ExpectedNodes) ledger.Expect(id);

        var retired = new List<string>();
        foreach (string id in options.RetiredNodes)
        {
            if (ledger.Retire(id)) retired.Add(id);
        }

        // After the ledger is populated, so the first /api/status already knows the fleet.
        server.Coverage = ledger.Snapshot;

        if (ledger.KnownNodes.Count == 0) return lines;

        lines.Add($"  fleet         {ledger.KnownNodes.Count} node(s) expected, "
            + $"silent after {ledger.SilenceThreshold.TotalSeconds:0}s");

        if (options.ExpectedNodes.Count > 0)
        {
            lines.Add($"                declared {string.Join(", ", options.ExpectedNodes)}");
        }

        if (remembered.Count > 0)
        {
            lines.Add($"                remembered {remembered.Count} from {options.CoverageStatePath}");
        }

        if (retired.Count > 0) lines.Add($"                retired {string.Join(", ", retired)}");

        lines.Add("                live at /api/status under \"coverage\"");
        return lines;
    }

    /// <summary>Remembers the fleet for the next run, or says why it could not.</summary>
    public static void Save(HostOptions options, TelemetryIngestPump? pump)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (pump is null || options.CoverageStatePath is null) return;

        if (CoverageStateFile.Write(options.CoverageStatePath, pump.Coverage.KnownNodeHistory) is { } failure)
        {
            Console.Error.WriteLine($"telemetry-host: {failure}");
            return;
        }

        Console.WriteLine($"           fleet of {pump.Coverage.KnownNodes.Count} node(s) remembered "
            + $"in {options.CoverageStatePath}");
    }
}
