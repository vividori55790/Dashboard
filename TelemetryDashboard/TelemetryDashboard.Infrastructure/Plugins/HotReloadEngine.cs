using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// Loads plugin assemblies on behalf of the folder watcher, tolerating the file still being written.
/// </summary>
/// <remarks>
/// <see cref="FileSystemWatcher"/> reports a file the moment the first byte lands, so the copy that
/// triggered the reload is routinely still open for exclusive write when the notification arrives.
/// The retry loop exists for exactly that window.
/// <para>
/// Only sharing and I/O faults are retried. A <see cref="BadImageFormatException"/> is deterministic —
/// the bytes will not become a valid assembly by waiting — so a corrupt DLL fails on the first attempt
/// instead of stalling the reload for <c>maxRetries * delayMs</c>.
/// </para>
/// <para>
/// Nothing here throws: a bad plugin file must never take down the dashboard that is monitoring live
/// hardware. <see cref="LastError"/> carries the reason for the operator log.
/// </para>
/// </remarks>
public sealed class HotReloadEngine
{
    private readonly AssemblyPluginAdapter _adapter;

    public HotReloadEngine() : this(new AssemblyPluginAdapter())
    {
    }

    public HotReloadEngine(AssemblyPluginAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    /// <summary>Why the most recent load attempt failed, or empty after a success.</summary>
    public string LastError { get; private set; } = string.Empty;

    /// <summary>Plugins produced by the most recent successful load.</summary>
    public IReadOnlyList<IPlugin> LastLoadedPlugins { get; private set; } = Array.Empty<IPlugin>();

    /// <summary>
    /// Attempts to load an assembly, retrying while the file is locked by the process that is
    /// writing it.
    /// </summary>
    /// <param name="assemblyPath">Full path to the plugin assembly.</param>
    /// <param name="maxRetries">Additional attempts after the first one. Values below zero count as zero.</param>
    /// <param name="delayMs">Pause between attempts, in milliseconds.</param>
    /// <returns><c>true</c> when the assembly was loaded; <c>false</c> for any failure.</returns>
    public bool TryLoadAssemblyWithRetry(string assemblyPath, int maxRetries, int delayMs)
    {
        LastError = string.Empty;
        LastLoadedPlugins = Array.Empty<IPlugin>();

        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            LastError = "No plugin assembly path was supplied.";
            return false;
        }

        int attempts = Math.Max(0, maxRetries) + 1;

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                LastLoadedPlugins = _adapter.LoadPlugin(assemblyPath);
                return true;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                LastError = $"'{Path.GetFileName(assemblyPath)}' was unavailable on attempt {attempt}/{attempts}: {ex.Message}";

                if (attempt < attempts && delayMs > 0) Thread.Sleep(delayMs);
            }
            catch (Exception ex)
            {
                // Permanent: a malformed image, a missing dependency, or a faulted plugin type.
                LastError = $"'{Path.GetFileName(assemblyPath)}' could not be loaded: {ex.Message}";
                return false;
            }
        }

        return false;
    }

    /// <summary>Releases the load context for a plugin that has been removed or replaced.</summary>
    public bool Unload(string assemblyPath) => _adapter.Unload(assemblyPath);

    /// <summary>
    /// True for failures that another attempt could plausibly clear — a file still being written,
    /// or one momentarily held by a virus scanner.
    /// </summary>
    /// <remarks>
    /// The "not found" I/O failures are excluded even though they derive from
    /// <see cref="IOException"/>: a path that does not exist will not start existing because the
    /// watcher waited, and the deletion that produced it is usually the point.
    /// </remarks>
    private static bool IsTransient(Exception exception) =>
        (exception is IOException and not FileNotFoundException and not DirectoryNotFoundException)
        || exception is UnauthorizedAccessException;
}
