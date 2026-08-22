using System;
using System.IO;
using Microsoft.Data.Sqlite;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Host.Startup;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Host.Archive;

/// <summary>
/// Executes <c>export &lt;archive.db&gt; --out &lt;file&gt;</c> and ends the process.
/// </summary>
/// <remarks>
/// Every refusal happens before anything is opened or written, and in the order that makes each one
/// possible to trust: the library check before the first SQLite call, the existence check before
/// the open that would otherwise create the file being asked about, and the layout check before the
/// open that would otherwise add its own tables to somebody's archive.
/// </remarks>
public static class ExportCommand
{
    /// <summary>Exit code for an archive that exists and holds nothing the window asked for.</summary>
    public const int ExitNoData = 74;

    /// <summary>Runs the subcommand named in <paramref name="args"/>, whose first word is 'export'.</summary>
    public static int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 1)
        {
            Console.Out.Write(ExportUsageText.Render());
            return 0;
        }

        ExportCommandLine command = ExportCommandLine.Parse(args);
        if (command.ShowHelp)
        {
            Console.Out.Write(ExportUsageText.Render());
            return 0;
        }

        if (command.Error is not null) return Refuse(command.Error, withUsage: true);
        if (!ExportDestination.TryResolve(command.OutputPath, out string target, out string? badTarget))
        {
            return Refuse(badTarget!, withUsage: false);
        }


        if (!File.Exists(command.ArchivePath))
        {
            return Refuse($"archive '{command.ArchivePath}' does not exist.", withUsage: false, ExitNoData);
        }

        // Before the first SQLite call. Without it a build missing the native library dies on an
        // unhandled type initializer instead of saying which file is absent.
        if (NativeDependencyCheck.ArchiveUnavailable() is { } missing) return Refuse(missing, withUsage: false);

        ArchiveLayoutKind layout;
        try
        {
            layout = ArchiveLayout.Detect(command.ArchivePath);
        }
        catch (SqliteException ex)
        {
            return Refuse($"'{command.ArchivePath}' cannot be read as a telemetry archive: {ex.Message}",
                withUsage: false, ExitNoData);
        }

        if (!TryOpen(command.ArchivePath, layout, out IDataLogger store, out string? refusal))
        {
            return Refuse(refusal!, withUsage: false, ExitNoData);
        }

        using (store as IDisposable)
        {
            return Write(store, target, command, layout);
        }
    }

    /// <summary>Opens the archive as whichever store actually wrote it.</summary>
    private static bool TryOpen(string path, ArchiveLayoutKind layout, out IDataLogger store, out string? refusal)
    {
        store = null!;
        refusal = null;

        switch (layout)
        {
            case ArchiveLayoutKind.Rows:
                store = new SqliteDataLogger(path);
                return true;
            case ArchiveLayoutKind.Tiered:
                // No retention policy: an export must never be the thing that prunes an archive.
                store = new TieredTelemetryStore(path);
                return true;
            case ArchiveLayoutKind.Ambiguous:
                refusal = $"'{path}' holds both a {ArchiveLayout.RowTable} and a {ArchiveLayout.TieredTable} "
                    + "table, so which one is the recording cannot be decided here.";
                return false;
            default:
                refusal = $"'{path}' is a SQLite database but holds no telemetry archive "
                    + $"(expected a {ArchiveLayout.RowTable} or {ArchiveLayout.TieredTable} table).";
                return false;
        }
    }

    /// <summary>Runs the export and reports what reached the file.</summary>
    private static int Write(IDataLogger store, string target, ExportCommandLine command, ArchiveLayoutKind layout)
    {
        int written;
        try
        {
            written = new MatlabArchiveExporter(store)
                .ExportAsync(target, command.Filter).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            return Refuse(ex.Message, withUsage: false);
        }

        if (written == 0)
        {
            // The exporter removes the destination rather than leaving a zero-byte MAT-file, which
            // loads as truncated rather than as empty. Saying so is the difference between an
            // operator widening their window and an operator hunting for a file that is not there.
            Console.Error.WriteLine($"telemetry-host: {ExportReport.Selection(command.Filter)} "
                + "matched no telemetry, so no file was written.");
            return ExitNoData;
        }

        Console.Out.Write(ExportReport.Render(written, target, command, layout));
        return 0;
    }

    private static int Refuse(string message, bool withUsage, int code = Program.ExitUsage)
    {
        Console.Error.WriteLine($"telemetry-host: {message}");
        if (withUsage) Console.Error.WriteLine($"Run '{ExportCommandLine.Verb} --help' for the accepted arguments.");
        return code;
    }
}
