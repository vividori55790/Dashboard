using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace TelemetryDashboard.Infrastructure.Updater;

/// <summary>
/// Runtime checks for the single-file portable build: native dependencies, resource extraction,
/// argument parsing and single-instance enforcement.
/// </summary>
/// <remarks>
/// A single-file publish extracts native libraries at startup, so failures here surface as
/// puzzling crashes deep inside a feature. Checking explicitly turns them into one clear message.
/// </remarks>
public sealed class PortablePackageChecker
{
    private Mutex? _instanceMutex;

    /// <summary>
    /// Full path of a native dependency, or null when this machine has no copy it could load.
    /// </summary>
    /// <param name="baseName">Name without prefix or extension, e.g. <c>e_sqlite3</c>.</param>
    /// <remarks>
    /// The one method on this class the host actually calls, and it exists because the failure it
    /// prevents is not a degraded feature but a dead process: measured on a build with
    /// <c>runtimes/win-x64/native/e_sqlite3.dll</c> deleted, the host printed its whole start-up
    /// banner, bound its port, advertised every endpoint, and then died with an unhandled
    /// TypeInitializationException the moment the archive was opened.
    /// </remarks>
    public string? LocateNativeLibrary(string baseName) => NativeLibraryProbe.Locate(baseName);

    /// <summary>True when a native library can be located by the name it has on disk.</summary>
    /// <remarks>
    /// Takes the exact file name, unlike <see cref="LocateNativeLibrary"/>. Kept because a caller
    /// checking for something whose name it already knows should not have to un-decorate it, and
    /// the search underneath is the same corrected one — which now includes
    /// <c>runtimes/&lt;rid&gt;/native/</c>. Looking only beside the executable and on PATH reported
    /// every framework-dependent build as missing its dependencies, and a start-up check that cries
    /// wolf is worse than no check at all.
    /// </remarks>
    public bool VerifyNativeDll(string dllFileName)
    {
        if (string.IsNullOrWhiteSpace(dllFileName)) return false;

        string baseName = Path.GetFileNameWithoutExtension(dllFileName);
        if (baseName.StartsWith("lib", StringComparison.Ordinal) && baseName.Length > 3)
        {
            baseName = baseName[3..];
        }

        return NativeLibraryProbe.Locate(baseName) is not null
            || File.Exists(Path.Combine(AppContext.BaseDirectory, dllFileName));
    }

    /// <summary>
    /// Prepares the extraction directory. Throws when the destination is unusable, so the caller
    /// can report it before the application depends on files that were never written.
    /// </summary>
    public string ExtractEmbeddedResources(string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new ArgumentException("Extraction directory must be provided.", nameof(targetDirectory));
        }

        if (targetDirectory.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || targetDirectory.Contains('|'))
        {
            throw new IOException($"Extraction path contains characters this platform rejects: {targetDirectory}");
        }

        Directory.CreateDirectory(targetDirectory);
        return targetDirectory;
    }

    /// <summary>Parses <c>--key=value</c> and <c>--flag</c> arguments.</summary>
    public Dictionary<string, string> ParseArgs(string[]? args)
    {
        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (args is null) return parsed;

        foreach (string raw in args)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            string argument = raw.TrimStart('-', '/');
            if (argument.Length == 0) continue;

            int separator = argument.IndexOf('=');
            if (separator > 0)
            {
                parsed[argument[..separator]] = argument[(separator + 1)..];
            }
            else
            {
                parsed[argument] = "true";
            }
        }

        return parsed;
    }

    /// <summary>
    /// Acquires the single-instance mutex. Returns true when this process owns it.
    /// </summary>
    public bool EnsureSingleInstance(string mutexName)
    {
        if (string.IsNullOrWhiteSpace(mutexName)) return true;

        // Release any mutex already held before taking another. Overwriting the field leaked the
        // previous handle, and the abandoned mutex stayed owned for the life of the process — a
        // second instance would then be refused startup by a lock nobody is using.
        ReleaseSingleInstance();

        try
        {
            _instanceMutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);
            return createdNew;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // Without a usable mutex the safest assumption is that this is the only instance.
            return true;
        }
    }

    public void ReleaseSingleInstance()
    {
        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not the owning thread; nothing to release.
        }
        finally
        {
            _instanceMutex?.Dispose();
            _instanceMutex = null;
        }
    }

    /// <summary>
    /// Reads the embedded configuration, substituting documented defaults when it cannot be parsed.
    /// </summary>
    public EmbeddedConfig LoadEmbeddedConfig(string? configJson) => EmbeddedConfigLoader.Load(configJson);
}
