using System;
using TelemetryDashboard.Core.Streaming;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Host.Ingest;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Deciding whether the archive can be opened, opening it, and saying where it went.
/// </summary>
/// <remarks>
/// One job in one place: the check has to come before the open, and the announcement after it, and
/// spreading those three across the entry point is how the check ends up on the wrong side of the
/// thing it guards.
/// </remarks>
public static class ArchiveSetup
{
    /// <summary>
    /// Opens the archive if one was asked for, or explains why this machine cannot.
    /// </summary>
    /// <returns>False only when the operator asked for an archive and cannot have one.</returns>
    /// <remarks>
    /// A run with no <c>--archive</c> succeeds with a null sink; that is not a failure, it is the
    /// default. What must never happen is succeeding with a null sink <em>after</em> an operator
    /// asked for one — they would find out when they came looking for the data.
    /// </remarks>
    public static bool TryOpen(
        HostOptions options, TelemetryStreamingServer server,
        out ArchiveSink? archive, out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(server);

        archive = null;
        refusal = null;
        if (options.ArchivePath is null) return true;

        // Before the open, not after. Without this a build missing SQLite's native library got all
        // the way through the banner and the socket bind and then died on an unhandled type
        // initializer -- every sign of a healthy start, and then a stack trace.
        if (NativeDependencyCheck.ArchiveUnavailable() is { } missing)
        {
            refusal = missing;
            return false;
        }

        try
        {
            archive = ArchiveSink.Open(options.ArchivePath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException
                                      or UnauthorizedAccessException)
        {
            refusal = ex.Message;
            return false;
        }

        if (archive is null) return true;

        server.Archive = archive.Store;
        Console.WriteLine($"  archive       {archive.DatabasePath}");
        Console.WriteLine("                queryable at /api/history?channel=<id>&from=<iso>&to=<iso>");
        return true;
    }
}
