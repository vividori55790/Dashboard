using System;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// The host services handed to a plugin at <see cref="IPlugin.Initialize"/>.
/// </summary>
/// <remarks>
/// Nothing implemented <see cref="IPluginContext"/> before this type existed, so every plugin was
/// initialised with a mock or not at all — the extension surface was a contract with no host side.
///
/// The context is deliberately narrow. A plugin receives the router, the serial manager and the
/// data logger, and nothing else: no window, no dispatcher, no direct access to the streaming
/// server. Widening it later is easy; taking a capability back once plugins depend on it is not.
/// </remarks>
public sealed class PluginHostContext : IPluginContext
{
    private readonly Action<string, PluginLogLevel> _sink;
    private readonly string _pluginId;

    /// <summary>
    /// Creates a context scoped to one plugin.
    /// </summary>
    /// <param name="pluginId">
    /// Identifies the plugin in every log line it writes. Required, because an operator reading
    /// the console must be able to tell a plugin's claim from the host's own.
    /// </param>
    /// <param name="logSink">
    /// Receives the plugin's messages, already tagged. Null routes them nowhere — which is a
    /// deliberate choice a host makes, not a default.
    /// </param>
    public PluginHostContext(
        string pluginId,
        IDataRouter router,
        ISerialManager serialManager,
        IDataLogger logger,
        Action<string, PluginLogLevel>? logSink = null)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            throw new ArgumentException("A plugin must be identified before it can log.", nameof(pluginId));
        }

        _pluginId = pluginId;
        Router = router ?? throw new ArgumentNullException(nameof(router));
        SerialManager = serialManager ?? throw new ArgumentNullException(nameof(serialManager));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sink = logSink ?? ((_, _) => { });
    }

    public IDataRouter Router { get; }

    public ISerialManager SerialManager { get; }

    public IDataLogger Logger { get; }

    /// <summary>Messages written so far, for diagnosing a plugin that goes quiet or floods.</summary>
    public long MessageCount { get; private set; }

    /// <summary>
    /// Records a message from the plugin, tagged with its id.
    /// </summary>
    /// <remarks>
    /// A throwing log sink is swallowed. A plugin calling <c>Log</c> must not be able to take the
    /// host down through a fault in the host's own console, and a plugin cannot be expected to
    /// guard against that on the host's behalf.
    /// </remarks>
    public void Log(string message, PluginLogLevel level = PluginLogLevel.Info)
    {
        MessageCount++;

        try
        {
            _sink($"[plugin:{_pluginId}] {message ?? string.Empty}", level);
        }
        catch (Exception)
        {
            // The console failed, not the plugin. Losing the line beats losing the process.
        }
    }
}
