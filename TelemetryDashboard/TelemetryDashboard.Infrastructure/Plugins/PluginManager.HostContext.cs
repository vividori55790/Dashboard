using System;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// The manager's host-services half: turning a discovered plugin into an initialised one without
/// the caller having to know how a context is built.
/// </summary>
/// <remarks>
/// Before this existed, <see cref="PluginManager.InitializePlugin(IPlugin, IPluginContext)"/> was
/// reachable only by a caller that already held a context — and the only callers that ever did were
/// tests holding <c>Mock.Of&lt;IPluginContext&gt;()</c>. A plugin discovered on disk therefore ran
/// against a host that did not exist. Giving the manager the services closes that gap at the layer
/// that already owns plugin lifetime, rather than asking every host to repeat the assembly.
/// </remarks>
public sealed partial class PluginManager
{
    private readonly PluginHostServices? _hostServices;

    /// <summary>
    /// Creates a manager that can only initialise plugins against a caller-supplied context.
    /// </summary>
    public PluginManager()
    {
    }

    /// <summary>
    /// Creates a manager that builds each plugin's context from <paramref name="hostServices"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="hostServices"/> is null.</exception>
    public PluginManager(PluginHostServices hostServices)
    {
        _hostServices = hostServices ?? throw new ArgumentNullException(nameof(hostServices));
    }

    /// <summary>Whether this manager can build contexts on its own.</summary>
    public bool HasHostServices => _hostServices is not null;

    /// <summary>
    /// Initialises <paramref name="plugin"/> against a context backed by the host's live services.
    /// </summary>
    /// <returns>The context the plugin was given, so a caller can inspect what it was handed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="plugin"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// This manager was constructed without host services. Thrown rather than substituting an inert
    /// context: a plugin initialised against a stub reports success and then does nothing, which is
    /// the exact failure this wiring exists to remove.
    /// </exception>
    /// <exception cref="Exception">Whatever the plugin threw, re-thrown after tear-down.</exception>
    public IPluginContext InitializePlugin(IPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        if (_hostServices is null)
        {
            throw new InvalidOperationException(
                "This PluginManager was built without host services, so it cannot supply a plugin "
                + "context. Construct it with PluginHostServices, or pass a context explicitly.");
        }

        // DescribeKey, not plugin.Id: a plugin broken enough to fault during initialisation may also
        // have a broken Id, and a context cannot be created without one.
        IPluginContext context = _hostServices.CreateContext(DescribeKey(plugin));
        InitializePlugin(plugin, context);
        return context;
    }
}
