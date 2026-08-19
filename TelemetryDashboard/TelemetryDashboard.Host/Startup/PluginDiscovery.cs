using System;
using System.Collections.Generic;
using System.IO;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Infrastructure.Plugins;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// The result of scanning a directory for plugin assemblies: what loaded, and why the rest did not.
/// </summary>
/// <remarks>
/// Discovery is separated from initialisation because the two fail for unrelated reasons — a file
/// that is not a managed assembly is a packaging problem, a plugin that throws from
/// <c>Initialize</c> is a plugin problem — and an operator who sees one number for both cannot tell
/// which they have.
/// <para>
/// Loading goes through <see cref="HotReloadEngine"/> rather than
/// <see cref="AssemblyPluginAdapter"/> directly: the adapter throws by design, and this scan must
/// survive one bad file and keep reading the directory. Every rejection is kept in
/// <see cref="Failures"/>, never discarded.
/// </para>
/// </remarks>
public sealed class PluginDiscovery
{
    private PluginDiscovery(string directory, bool directoryExists)
    {
        Directory = directory;
        DirectoryExists = directoryExists;
    }

    /// <summary>Directory that was scanned.</summary>
    public string Directory { get; }

    /// <summary>Whether that directory was there at all.</summary>
    public bool DirectoryExists { get; }

    /// <summary>Assembly files the scan looked at, loadable or not.</summary>
    public int AssembliesScanned { get; private set; }

    /// <summary>Instances found across every assembly that loaded.</summary>
    public IReadOnlyList<IPlugin> Plugins => _plugins;

    /// <summary>One line per assembly that could not be loaded, with the reason.</summary>
    public IReadOnlyList<string> Failures => _failures;

    private readonly List<IPlugin> _plugins = new();
    private readonly List<string> _failures = new();

    /// <summary>
    /// Scans <paramref name="directory"/> for <c>*.dll</c> and instantiates the plugins it finds.
    /// </summary>
    /// <remarks>
    /// A missing directory is reported through <see cref="DirectoryExists"/> rather than created.
    /// Creating it would turn "you have not deployed any plugins" into an empty folder that looks
    /// deliberate, and the host has nothing to put in it.
    /// </remarks>
    public static PluginDiscovery Scan(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("A plugin directory is required.", nameof(directory));
        }

        string full = Path.GetFullPath(directory);
        var discovery = new PluginDiscovery(full, System.IO.Directory.Exists(full));
        if (!discovery.DirectoryExists) return discovery;

        var engine = new HotReloadEngine();

        foreach (string file in System.IO.Directory.EnumerateFiles(full, "*.dll", SearchOption.TopDirectoryOnly))
        {
            discovery.AssembliesScanned++;

            // No retries: nothing is copying into this directory during start-up, so a lock here is
            // a real problem and waiting on it would only delay the host.
            if (engine.TryLoadAssemblyWithRetry(file, maxRetries: 0, delayMs: 0))
            {
                discovery._plugins.AddRange(engine.LastLoadedPlugins);
            }
            else
            {
                discovery._failures.Add(engine.LastError);
            }
        }

        return discovery;
    }
}
