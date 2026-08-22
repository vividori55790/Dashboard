using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace TelemetryDashboard.Infrastructure.Updater;

/// <summary>
/// Finds a native library where .NET actually puts one.
/// </summary>
/// <remarks>
/// Split out because the search is the part that was wrong. Looking beside the executable and on
/// PATH is right for a self-contained single-file build, where the runtime extracts natives next to
/// the host — and wrong for every framework-dependent build, where they sit under
/// <c>runtimes/&lt;rid&gt;/native/</c> and nothing copies them up. A checker that searched only the
/// first two answered "missing" for a perfectly working install, which is worse than not checking:
/// a false alarm at start-up trains an operator to ignore the real one.
/// <para>
/// The platform's own naming is applied rather than demanded from the caller. The same dependency
/// is <c>e_sqlite3.dll</c>, <c>libe_sqlite3.so</c> and <c>libe_sqlite3.dylib</c> depending on where
/// the host runs, and a caller that had to spell that out would get it right on the machine it was
/// written on.
/// </para>
/// </remarks>
public static class NativeLibraryProbe
{
    /// <summary>Full path of the library, or null when this machine has no copy it could load.</summary>
    /// <param name="baseName">Library name without prefix or extension, e.g. <c>e_sqlite3</c>.</param>
    public static string? Locate(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName)) return null;

        string fileName = PlatformFileName(baseName);

        // Beside the executable: a self-contained single-file publish extracts here, and so does an
        // ordinary self-contained one.
        string beside = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(beside)) return beside;

        foreach (string candidate in RuntimeAssetPaths(fileName))
        {
            if (File.Exists(candidate)) return candidate;
        }

        return OnSearchPath(fileName);
    }

    /// <summary>The file name this platform gives <paramref name="baseName"/>.</summary>
    public static string PlatformFileName(string baseName)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return baseName + ".dll";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "lib" + baseName + ".dylib";
        return "lib" + baseName + ".so";
    }

    /// <summary>
    /// <c>runtimes/&lt;rid&gt;/native/&lt;file&gt;</c> for every rid folder this build carries.
    /// </summary>
    /// <remarks>
    /// Every folder is offered rather than only the one matching
    /// <see cref="RuntimeInformation.RuntimeIdentifier"/>, because that identifier and the folder
    /// names disagree in practice — a host reporting <c>win10-x64</c> loads assets from
    /// <c>win-x64</c>. The file name already carries the platform, so a Linux <c>.so</c> can never
    /// satisfy a Windows probe; the architecture is checked as well so an arm64 copy is not read as
    /// proof that an x64 host can load one.
    /// </remarks>
    private static IEnumerable<string> RuntimeAssetPaths(string fileName)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "runtimes");
        if (!Directory.Exists(root)) yield break;

        string architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

        string[] folders;
        try
        {
            folders = Directory.GetDirectories(root);
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (string folder in folders)
        {
            string rid = Path.GetFileName(folder);
            if (!rid.EndsWith("-" + architecture, StringComparison.OrdinalIgnoreCase)) continue;

            yield return Path.Combine(folder, "native", fileName);
        }
    }

    private static string? OnSearchPath(string fileName)
    {
        string? search = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(search)) return null;

        foreach (string directory in search.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(directory.Trim(), fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry is skipped rather than aborting the scan.
            }
        }

        return null;
    }
}
