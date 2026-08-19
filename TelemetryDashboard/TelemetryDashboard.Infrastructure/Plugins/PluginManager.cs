using System;
using System.Collections.Generic;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// Tracks the lifecycle of loaded <see cref="IPlugin"/> instances.
/// </summary>
/// <remarks>
/// A plugin that throws from <see cref="IPlugin.Initialize"/> is torn down and recorded here, but the
/// exception is deliberately re-thrown. Whoever asked for the plugin — the extension store, a folder
/// scan, a hot-reload — is the only layer that knows whether a failure should be shown to the
/// operator, retried, or ignored; swallowing it here would leave the plugin silently absent with no
/// explanation anywhere.
/// <para>
/// Tear-down still happens first. <c>Initialize</c> may have opened a port, spawned a thread, or
/// subscribed to the router before it faulted, and those survive the exception; calling
/// <see cref="IPlugin.Shutdown"/> gives the plugin its one chance to release them.
/// </para>
/// <para>
/// The half of this class that builds a real <see cref="IPluginContext"/> from the host's running
/// services lives in <c>PluginManager.HostContext.cs</c>, so neither file grows past the point
/// where it can be read in one pass.
/// </para>
/// </remarks>
public sealed partial class PluginManager
{
    private readonly object _gate = new();
    private readonly List<IPlugin> _active = new();
    private readonly Dictionary<string, string> _failed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Plugins that initialised successfully and have not been shut down.</summary>
    public IReadOnlyList<IPlugin> ActivePlugins
    {
        get { lock (_gate) { return new List<IPlugin>(_active); } }
    }

    /// <summary>Plugin id to failure reason, for the diagnostics pane.</summary>
    public IReadOnlyDictionary<string, string> FailedPlugins
    {
        get { lock (_gate) { return new Dictionary<string, string>(_failed, StringComparer.OrdinalIgnoreCase); } }
    }

    /// <summary>
    /// Initialises <paramref name="plugin"/> and adds it to the active set.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="plugin"/> is null.</exception>
    /// <exception cref="Exception">Whatever the plugin threw, re-thrown after tear-down.</exception>
    public void InitializePlugin(IPlugin plugin, IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        try
        {
            plugin.Initialize(context);
        }
        catch (Exception ex)
        {
            RecordFailure(plugin, ex);
            throw;
        }

        lock (_gate)
        {
            _failed.Remove(DescribeKey(plugin));
            if (!_active.Contains(plugin)) _active.Add(plugin);
        }
    }

    /// <summary>Shuts a plugin down and drops it from the active set.</summary>
    /// <returns><c>false</c> when the plugin was not active.</returns>
    public bool ShutdownPlugin(IPlugin plugin)
    {
        if (plugin is null) return false;

        lock (_gate)
        {
            if (!_active.Remove(plugin)) return false;
        }

        SafeShutdown(plugin);
        return true;
    }

    /// <summary>Shuts every active plugin down; one faulting plugin does not block the rest.</summary>
    public void ShutdownAll()
    {
        List<IPlugin> plugins;
        lock (_gate)
        {
            plugins = new List<IPlugin>(_active);
            _active.Clear();
        }

        foreach (IPlugin plugin in plugins) SafeShutdown(plugin);
    }

    private void RecordFailure(IPlugin plugin, Exception failure)
    {
        lock (_gate)
        {
            _active.Remove(plugin);
            _failed[DescribeKey(plugin)] = failure.Message;
        }

        SafeShutdown(plugin);
    }

    /// <summary>
    /// Shutdown is best effort: it runs while the host is already handling a failure, or is tearing
    /// the application down, and neither situation is improved by a second exception.
    /// </summary>
    private static void SafeShutdown(IPlugin plugin)
    {
        try
        {
            plugin.Shutdown();
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Identifies a plugin for the failure log, falling back to its type when the id is unusable —
    /// a plugin broken enough to fault during initialisation may also have a broken <c>Id</c>.
    /// </summary>
    private static string DescribeKey(IPlugin plugin)
    {
        string fallback = plugin.GetType().FullName ?? plugin.GetType().Name;

        try
        {
            return string.IsNullOrWhiteSpace(plugin.Id) ? fallback : plugin.Id;
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
