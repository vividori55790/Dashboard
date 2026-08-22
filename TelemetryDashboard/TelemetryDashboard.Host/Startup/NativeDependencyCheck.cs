using System;
using TelemetryDashboard.Infrastructure.Updater;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Checks that an optional feature's native library is actually on this machine before the host
/// promises the feature.
/// </summary>
/// <remarks>
/// Measured on a build with <c>runtimes/win-x64/native/e_sqlite3.dll</c> removed, which is what a
/// trimmed publish or a half-extracted portable package leaves behind: the host printed its entire
/// start-up banner, bound its port, advertised all thirteen endpoints, and then died with an
/// unhandled <c>TypeInitializationException</c> out of <c>SqliteConnection</c>. Not a degraded
/// archive — a dead process, after every sign of a healthy start.
/// <para>
/// So the check runs before the feature is opened, and a missing dependency ends the run with a
/// sentence instead of a stack trace. It ends rather than continuing without the archive for the
/// reason <see cref="Ingest.ArchiveSink"/> already gives: an operator who passed <c>--archive</c>
/// and got a run with no archive finds out when they come looking for the data.
/// </para>
/// </remarks>
public static class NativeDependencyCheck
{
    /// <summary>SQLite's native library, without prefix or extension.</summary>
    public const string SqliteLibrary = "e_sqlite3";

    /// <summary>
    /// Why the archive cannot be opened on this machine, or null when it can.
    /// </summary>
    /// <remarks>
    /// Returns a sentence rather than throwing, because the caller's job is to print it and choose
    /// an exit code. The path where the library <em>was</em> found is not reported: a check that
    /// passes should be silent, and a start-up banner that recites its own dependencies is noise
    /// on every successful run to serve the rare failing one.
    /// </remarks>
    public static string? ArchiveUnavailable()
    {
        var checker = new PortablePackageChecker();
        return checker.LocateNativeLibrary(SqliteLibrary) is null ? Refusal(SqliteLibrary) : null;
    }

    /// <summary>The sentence printed when a dependency is absent.</summary>
    /// <remarks>
    /// Its own method so it can be read by a test on a machine where the library is present -- and
    /// so the three things it has to carry stay together: which file, where this looked, and what
    /// the operator can do about it. A refusal missing the last of those sends somebody to a search
    /// engine.
    /// </remarks>
    public static string Refusal(string baseName) =>
        $"--archive needs SQLite's native library ({NativeLibraryProbe.PlatformFileName(baseName)}), "
        + "and this build has no copy of it beside the executable, under runtimes/, or on the path. "
        + "A trimmed publish or a partly extracted portable package does this. Reinstall the package, "
        + "or run without --archive.";
}
