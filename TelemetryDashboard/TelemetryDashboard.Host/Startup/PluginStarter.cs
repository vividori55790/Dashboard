using System;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Infrastructure.Plugins;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Brings one discovered plugin up: context, initialisation, router registration, and the line the
/// operator reads about it.
/// </summary>
/// <remarks>
/// <see cref="PluginManager"/> re-throws whatever a plugin threw, precisely so the layer that asked
/// for the plugin decides what to do about it. This is that layer, and its decision is: report the
/// failure on stderr and keep loading the rest. One broken third-party plugin must not cost the
/// operator every working one, and it must not pass unmentioned either.
/// </remarks>
internal static class PluginStarter
{
    /// <summary>Initialises <paramref name="plugin"/> and registers it with the router on success.</summary>
    /// <remarks>
    /// Registration follows initialisation, never precedes it. A plugin registered first would
    /// receive packets through <see cref="IPlugin.OnPacketReceived"/> and
    /// <see cref="IPlugin.TryCustomParse"/> before it had been given a context to handle them with.
    /// </remarks>
    internal static void Start(PluginManager manager, IPlugin plugin, DataRouter? router)
    {
        try
        {
            manager.InitializePlugin(plugin);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [plugin-host] {Identify(plugin)} failed to initialise: {ex.Message}");
            return;
        }

        router?.RegisterPlugin(plugin);
        Console.WriteLine($"                {Identify(plugin),-24}{Name(plugin)}");
    }

    /// <summary>
    /// The plugin's id, falling back to its type when the id cannot be read.
    /// </summary>
    /// <remarks>
    /// A plugin broken enough to fault during initialisation may also have a broken <c>Id</c>, and
    /// the failure line is worth more than the property access that would replace it with a stack
    /// trace from the reporting code itself.
    /// </remarks>
    private static string Identify(IPlugin plugin)
    {
        try
        {
            return string.IsNullOrWhiteSpace(plugin.Id) ? plugin.GetType().Name : plugin.Id;
        }
        catch (Exception)
        {
            return plugin.GetType().Name;
        }
    }

    /// <summary>The plugin's display name and version, or a note that it would not say.</summary>
    private static string Name(IPlugin plugin)
    {
        try
        {
            return $"{plugin.Name,-32}{plugin.Version}";
        }
        catch (Exception)
        {
            return "(name and version unreadable)";
        }
    }
}
