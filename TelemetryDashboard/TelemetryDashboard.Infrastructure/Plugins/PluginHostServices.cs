using System;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// The live host objects a plugin is allowed to reach, and the factory that scopes them to one
/// plugin as an <see cref="IPluginContext"/>.
/// </summary>
/// <remarks>
/// <see cref="PluginHostContext"/> existed with no caller: every <see cref="IPlugin.Initialize"/>
/// in the product was reached only from a test holding a mock, so the extension surface was a
/// contract with a host side that nothing ever built. This type is the missing half — one object a
/// host assembles once from its running services and hands to <see cref="PluginManager"/>, so the
/// wiring lives in one place instead of being re-derived at every call site that loads a plugin.
/// <para>
/// The services are held, not copied, deliberately. A plugin must see the same router the ingest
/// pump is publishing through and the same serial manager holding the open ports; handing it a
/// freshly constructed pair would satisfy the compiler and give the plugin a view of a system that
/// nothing is driving.
/// </para>
/// </remarks>
public sealed class PluginHostServices
{
    private readonly Action<string, PluginLogLevel>? _logSink;

    /// <summary>
    /// Captures the services every plugin of this host will share.
    /// </summary>
    /// <param name="router">The router packets are actually being routed through.</param>
    /// <param name="serialManager">The manager owning the host's serial ports.</param>
    /// <param name="logger">Durable storage plugins may write to and query.</param>
    /// <param name="logSink">
    /// Where plugin log lines go. Null routes them nowhere, which a host may choose but never gets
    /// by accident: a silent plugin and a plugin whose output was discarded look identical.
    /// </param>
    /// <exception cref="ArgumentNullException">Any service is null.</exception>
    public PluginHostServices(
        IDataRouter router,
        ISerialManager serialManager,
        IDataLogger logger,
        Action<string, PluginLogLevel>? logSink = null)
    {
        Router = router ?? throw new ArgumentNullException(nameof(router));
        SerialManager = serialManager ?? throw new ArgumentNullException(nameof(serialManager));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logSink = logSink;
    }

    /// <summary>The router handed to every context this instance creates.</summary>
    public IDataRouter Router { get; }

    /// <summary>The serial manager handed to every context this instance creates.</summary>
    public ISerialManager SerialManager { get; }

    /// <summary>The data logger handed to every context this instance creates.</summary>
    public IDataLogger Logger { get; }

    /// <summary>
    /// Builds the context for one plugin.
    /// </summary>
    /// <param name="pluginId">
    /// Tags every line the plugin logs. Required — an operator reading the console must be able to
    /// tell a plugin's claim from the host's own.
    /// </param>
    /// <returns>A context bound to this host's services and to <paramref name="pluginId"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="pluginId"/> is blank.</exception>
    public IPluginContext CreateContext(string pluginId) =>
        new PluginHostContext(pluginId, Router, SerialManager, Logger, _logSink);
}
