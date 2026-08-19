using System;
using System.IO;

namespace TelemetryDashboard.Tests.TestUtilities;

/// <summary>
/// A private temporary directory that deletes itself, for tests that must touch real files.
/// </summary>
/// <remarks>
/// The storage tests cannot be written against an abstraction: what they are checking is what
/// SQLite and the file system actually do with a bound NaN, a rolled-back transaction and a
/// non-database file. That means real paths, and a real path left behind after a failed test is a
/// database another run can open — so cleanup happens in <see cref="Dispose"/> rather than at the
/// end of the test body, where an assertion failure would skip it.
/// </remarks>
public sealed class TempWorkspace : IDisposable
{
    /// <summary>Creates and owns a new directory under the system temp folder.</summary>
    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "td-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>Full path of the directory this workspace owns.</summary>
    public string Root { get; }

    /// <summary>Path to <paramref name="fileName"/> inside the workspace. The file is not created.</summary>
    public string File(string fileName) => Path.Combine(Root, fileName);

    /// <summary>Deletes the directory and everything in it.</summary>
    /// <remarks>
    /// A lingering handle is swallowed rather than thrown: failing here would replace a real test
    /// result with a cleanup error, and the directory is disposable temp state either way.
    /// </remarks>
    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
