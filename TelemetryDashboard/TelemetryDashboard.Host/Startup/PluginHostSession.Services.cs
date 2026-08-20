using System;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Infrastructure.Plugins;
using TelemetryDashboard.Infrastructure.Serial;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// The session's service-assembly half: what a plugin is handed, and how one extension is brought
/// up against it.
/// </summary>
/// <remarks>
/// Split from the session's lifecycle so neither file grows past the point where it can be read in
/// one pass. The division follows the two questions a reader arrives with: "what runs, and in what
/// order" is in <c>PluginHostSession.cs</c>; "what does a plugin actually get" is here.
/// </remarks>
public sealed partial class PluginHostSession
{
    /// <summary>
    /// Starts one extension's plugin, recording an initialisation failure against the extension.
    /// </summary>
    /// <remarks>
    /// Without this the extension would be listed as loaded — its assembly did load — while the
    /// plugin inside it never initialised. That gap between "installed" and "running", with nothing
    /// naming it, is the failure the extension report exists to close.
    /// </remarks>
    private void StartExtension(ExtensionLoader extensions, IPlugin plugin, DataRouter? router)
    {
        string? failure = PluginStarter.Start(_manager!, plugin, router);
        if (failure is null) return;

        string? owner = extensions.OwnerOf(plugin);
        if (owner is not null) extensions.Store.RecordLoadFailure(owner, failure);
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
}
