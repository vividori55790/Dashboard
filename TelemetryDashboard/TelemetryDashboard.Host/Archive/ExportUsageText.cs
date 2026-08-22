using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Host.Archive;

/// <summary>The <c>export</c> help screen.</summary>
/// <remarks>
/// Beside the parser, for the reason <see cref="Configuration.UsageText"/> is beside its own: an
/// option cannot be added without the line documenting it sitting one file away.
/// </remarks>
public static class ExportUsageText
{
    /// <summary>Renders the help screen.</summary>
    public static string Render() => $"""
        Writes a window of a telemetry archive to a MATLAB MAT-file.

        Usage:
          TelemetryDashboard.Host {ExportCommandLine.Verb} <archive.db> --out <file{MatlabArchiveExporter.Extension}> [options]

        The archive is a database this host wrote with --archive. Either layout is read; which one
        a file holds is worked out from the file itself, so the flags that produced it do not have
        to be remembered. Nothing is written back to it, and --retain plays no part: an export
        never prunes.

        One matrix per channel, named after it, with two columns: MATLAB datenum, then the value.
        Plot one with plot(Vout(:,1), Vout(:,2)); datetick('x').

        A channel whose name is not a legal MATLAB identifier is renamed to one, and a name that
        collides after renaming is suffixed rather than overwritten. When the exported samples come
        from more than one node, every matrix is prefixed with the node it belongs to -- otherwise
        two converters both reporting Vout would land in one column of interleaved readings.

        Options:
          --out, -o <file>      Where to write. Required, because guessing a destination for a file
                                write is not this command's decision to make. A path with no
                                extension gets {MatlabArchiveExporter.Extension}.
          --from <iso>          Earliest sample to include, as 2026-08-21T14:00:00Z. A stamp with
                                no zone is read as UTC. A stamp with no date is refused rather
                                than answered with today, which is a day the archive may never
                                have covered.
          --to <iso>            Latest sample to include.
          --node <id>           Only this node.
          --channel <id>        Only this channel. --variable is accepted for the same thing, since
                                that is what /api/history calls it.
          --limit <n>           Stop after n samples, oldest first. Everything, by default: the
                                query layer's own default is 1000, which would quietly export the
                                beginning of a long recording under a message saying it succeeded.
          --help, -h            This screen.

        Exit codes:
          0                     A file was written.
          64                    The command line, the destination or the file could not be used.
          {ExportCommand.ExitNoData}                    The archive is absent, unreadable, or holds nothing selected.

        Examples:
          TelemetryDashboard.Host {ExportCommandLine.Verb} bench.db --out bench{MatlabArchiveExporter.Extension}
          TelemetryDashboard.Host {ExportCommandLine.Verb} bench.db --out fault --from 2026-08-21T14:00:00Z --to 2026-08-21T14:05:00Z
          TelemetryDashboard.Host {ExportCommandLine.Verb} bench.db --out rail --channel Vout --node PSFB-01

        """;
}
