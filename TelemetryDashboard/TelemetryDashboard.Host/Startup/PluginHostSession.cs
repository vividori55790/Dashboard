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
/// Two sources feed it. The <c>plugins/</c> folder is the unmanaged drop path, unchanged; the
/// extension store is the managed one, where an extension has an id, a verified hash and an
/// enable/disable state that survives a restart. Both end up in one <see cref="PluginManager"/>
/// sharing one context, because a plugin cannot be expected to behave differently depending on
/// which directory a deployment happened to put it in.
/// </para>
/// </remarks>
public sealed partial class PluginHostSession : IDisposable
{
    /// <summary>File name of the SQLite store plugins log through, beside the executable.</summary>
    public const string StoreFileName = "plugin-telemetry.db";

    private readonly List<IDisposable> _owned = new();
    private PluginManager? _manager;
    private ExtensionLoader? _extensions;

    private PluginHostSession()
    {
    }

    /// <summary>The API version this host advertises to extensions, from its own assembly version.</summary>
    /// <remarks>
    /// Read rather than declared, so it cannot claim a compatibility the shipped binary does not
    /// have. An extension's <c>minApiVersion</c> is compared against this.
    /// </remarks>
    public static string HostApiVersion =>
        typeof(PluginHostSession).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Plugins that initialised and are now registered with the router.</summary>
    public IReadOnlyList<IPlugin> ActivePlugins => _manager?.ActivePlugins ?? Array.Empty<IPlugin>();

    /// <summary>Plugin id to the reason it did not start.</summary>
    public IReadOnlyDictionary<string, string> FailedPlugins =>
        _manager?.FailedPlugins ?? new Dictionary<string, string>();

    /// <summary>Everything the extension store held this run, and what became of it.</summary>
    public ExtensionLoader? Extensions => _extensions;

    /// <summary>Discovers, initialises and reports the host's plugins and installed extensions.</summary>
    /// <param name="options">Supplies the plugin and extension directories, if named.</param>
    /// <param name="router">The router ingest publishes through, or null when nothing is attached.</param>
    /// <param name="serialManager">The manager holding the open port, or null when none is open.</param>
    public static PluginHostSession Start(HostOptions options, DataRouter? router, ISerialManager? serialManager)
    {
        var session = new PluginHostSession();
        string directory = options.PluginDirectory ?? Path.Combine(AppContext.BaseDirectory, "plugins");

        PluginDiscovery discovery = PluginDiscovery.Scan(directory);
        ExtensionLoader extensions = ExtensionLoader.Load(
            options.ExtensionDirectory ?? ExtensionCommandLine.DefaultDirectory(), HostApiVersion);
        session._extensions = extensions;

        PluginHostReport.PrintDiscovery(discovery);

        if (discovery.Plugins.Count > 0 || extensions.Plugins.Count > 0)
        {
            session.Initialize(discovery, extensions, router, serialManager);
        }

        Console.WriteLine();
        ExtensionStartupReport.Print(extensions);
        Console.WriteLine();
        return session;
    }

    /// <summary>Shuts every plugin down and releases what this session opened.</summary>
    /// <remarks>
    /// Idempotent. Load contexts are released after the plugins stop, which is what lets an
    /// <c>extensions remove</c> delete the assembly once this host has exited.
    /// </remarks>
    public void Dispose()
    {
        _manager?.ShutdownAll();
        _extensions?.UnloadAll();

        // Reverse order: the store is opened last and must outlive the plugins writing to it.
        for (int i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
        _owned.Clear();
    }

    private void Initialize(PluginDiscovery discovery, ExtensionLoader extensions, DataRouter? router, ISerialManager? serialManager)
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
        PluginHostReport.PrintServices(storePath, router is not null, serialManager is not null);

        foreach (IPlugin plugin in discovery.Plugins) PluginStarter.Start(_manager, plugin, router);
        foreach (IPlugin plugin in extensions.Plugins) StartExtension(extensions, plugin, router);

        Console.WriteLine(
            $"                {_manager.ActivePlugins.Count} initialised, {_manager.FailedPlugins.Count} failed.");
    }

}
