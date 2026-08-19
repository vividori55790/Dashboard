using System;
using System.Collections.Generic;
using System.IO;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Infrastructure.Plugins;
using TelemetryDashboard.Infrastructure.Serial;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// The host's live plugins: discovered on disk, initialised against the running services, and shut
/// down again when the host drains.
/// </summary>
/// <remarks>
/// This is the caller <see cref="PluginHostContext"/> never had. Until it existed, every
/// <see cref="IPlugin.Initialize"/> in the product was reached only from a test holding a mock, so
/// a plugin deployed into <c>plugins/</c> was handed nothing or never loaded at all — the whole
/// extension surface was decorative.
/// <para>
/// The router and serial manager are passed in rather than constructed here, so plugins see the
/// objects ingest is actually driving. When the host has no source there is nothing to attach to;
/// the session says so on the console instead of implying a live feed.
/// </para>
/// </remarks>
public sealed class PluginHostSession : IDisposable
{
    /// <summary>File name of the SQLite store plugins log through, beside the executable.</summary>
    /// <remarks>
    /// Not inside <c>--plugin-dir</c>, which may be a read-only or package-managed drop folder, and
    /// opened only once a plugin has actually been found so an unused host writes no database.
    /// </remarks>
    public const string StoreFileName = "plugin-telemetry.db";

    private readonly List<IDisposable> _owned = new();
    private PluginManager? _manager;

    private PluginHostSession()
    {
    }

    /// <summary>Plugins that initialised and are now registered with the router.</summary>
    public IReadOnlyList<IPlugin> ActivePlugins => _manager?.ActivePlugins ?? Array.Empty<IPlugin>();

    /// <summary>Plugin id to the reason it did not start.</summary>
    public IReadOnlyDictionary<string, string> FailedPlugins =>
        _manager?.FailedPlugins ?? new Dictionary<string, string>();

    /// <summary>Discovers, initialises and reports the host's plugins.</summary>
    /// <param name="options">Supplies the plugin directory, if one was named.</param>
    /// <param name="router">The router ingest publishes through, or null when nothing is attached.</param>
    /// <param name="serialManager">The manager holding the open port, or null when none is open.</param>
    public static PluginHostSession Start(HostOptions options, DataRouter? router, ISerialManager? serialManager)
    {
        var session = new PluginHostSession();
        string directory = options.PluginDirectory ?? Path.Combine(AppContext.BaseDirectory, "plugins");
        PluginDiscovery discovery = PluginDiscovery.Scan(directory);

        Console.WriteLine($"  plugins       {discovery.Directory}");
        foreach (string failure in discovery.Failures) Console.Error.WriteLine($"  [plugin-load] {failure}");

        if (!discovery.DirectoryExists)
        {
            Console.WriteLine("                directory not present -- no plugin was loaded.");
        }
        else if (discovery.Plugins.Count == 0)
        {
            Console.WriteLine($"                {discovery.AssembliesScanned} assemblies scanned, no IPlugin found.");
        }
        else
        {
            session.Initialize(discovery, router, serialManager);
        }

        Console.WriteLine();
        return session;
    }

    /// <summary>Shuts every plugin down and releases what this session opened.</summary>
    /// <remarks>
    /// Idempotent. The shutdown path calls this explicitly so the plugins stop in the right order
    /// relative to the ingest drain, while the <c>using</c> in the entry point still covers the
    /// paths that never reach it.
    /// </remarks>
    public void Dispose()
    {
        _manager?.ShutdownAll();

        // Reverse order: the store is opened last and must outlive the plugins writing to it.
        for (int i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
        _owned.Clear();
    }

    private void Initialize(PluginDiscovery discovery, DataRouter? router, ISerialManager? serialManager)
    {
        string storePath = Path.Combine(AppContext.BaseDirectory, StoreFileName);
        PluginHostServices services;

        try
        {
            services = BuildServices(storePath, router, serialManager);
        }
        catch (Exception ex)
        {
            // Reported, not swallowed: without a store there is no IDataLogger, so no plugin can be
            // given a context, and the operator is told why rather than left to notice the silence.
            Console.Error.WriteLine($"  [plugin-host] store '{storePath}' unusable: {ex.Message}");
            Console.Error.WriteLine("  [plugin-host] no plugin was initialised.");
            return;
        }

        _manager = new PluginManager(services);
        Console.WriteLine($"                store  {storePath}");
        Console.WriteLine($"                router {Describe(router is not null, "ingest attached")}"
            + $", serial {Describe(serialManager is not null, "port open")}");

        foreach (IPlugin plugin in discovery.Plugins) PluginStarter.Start(_manager, plugin, router);

        Console.WriteLine(
            $"                {_manager.ActivePlugins.Count} initialised, {_manager.FailedPlugins.Count} failed.");
    }

    private PluginHostServices BuildServices(string storePath, DataRouter? router, ISerialManager? serialManager)
    {
        var logger = new SqliteDataLogger(storePath);
        _owned.Add(logger);

        // A manager of the host's own only when ingest brought none: plugins must be able to
        // enumerate ports, and an ISerialManager is not optional in the context.
        ISerialManager serial = serialManager ?? Own(new MultiPortSerialManager());
        return new PluginHostServices(router ?? new DataRouter(), serial, logger, PluginConsole.WriteLogLine);
    }

    private MultiPortSerialManager Own(MultiPortSerialManager manager)
    {
        _owned.Add(manager);
        return manager;
    }

    private static string Describe(bool live, string why) => live ? $"live -- {why}" : $"idle -- no {why}";
}
