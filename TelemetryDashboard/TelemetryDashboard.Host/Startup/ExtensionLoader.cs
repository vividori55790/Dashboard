using System;
using System.Collections.Generic;
using System.IO;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Plugins;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Turns the extension store into plugin instances the host can initialise, and an account of
/// everything it did not load.
/// </summary>
/// <remarks>
/// The account is the point. An extension can be absent from a running host for four unrelated
/// reasons — switched off, too old for this API, missing from disk, or broken on load — and an
/// operator who sees only the ones that worked cannot tell which happened. Every skip carries the
/// reason it was skipped, and the start-up report prints all of them.
/// <para>
/// Compatibility is decided by <see cref="ExtensionRegistry"/> rather than by an inline version
/// comparison here, so the rule that a host on API <c>x</c> may load an extension requiring
/// <c>y</c> lives in exactly one place and is the same rule the catalogue applies.
/// </para>
/// </remarks>
public sealed class ExtensionLoader
{
    /// <summary>Directory used when no <c>--extension-dir</c> was given.</summary>
    public const string DefaultDirectoryName = "extensions";

    private readonly HotReloadEngine _engine = new();
    private readonly List<string> _loadedPaths = new();
    private readonly Dictionary<string, IPlugin> _owners = new(StringComparer.OrdinalIgnoreCase);

    private ExtensionLoader(ExtensionStore store)
    {
        Store = store;
    }

    /// <summary>The store that was read.</summary>
    public ExtensionStore Store { get; }

    /// <summary>Descriptors of everything installed, compatible or not.</summary>
    public ExtensionRegistry Registry { get; } = new();

    /// <summary>Plugin instances from enabled, compatible extensions, awaiting initialisation.</summary>
    public IReadOnlyList<IPlugin> Plugins => _plugins;

    /// <summary>Extension id to the reason it contributed no plugin, in installation order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Skipped => _skipped;

    private readonly List<IPlugin> _plugins = new();
    private readonly List<KeyValuePair<string, string>> _skipped = new();

    /// <summary>Reads <paramref name="directory"/> and loads what is enabled and compatible.</summary>
    /// <param name="directory">Store root; a missing directory yields an empty loader, not a failure.</param>
    /// <param name="hostApiVersion">The API version this host advertises to extensions.</param>
    public static ExtensionLoader Load(string directory, string hostApiVersion)
    {
        var loader = new ExtensionLoader(new ExtensionStore(directory));

        foreach (InstalledExtension installed in loader.Store.Extensions)
        {
            loader.Registry.Register(Describe(installed));
        }

        var compatible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ExtensionDescriptor descriptor in loader.Registry.GetCompatibleExtensions(hostApiVersion))
        {
            compatible.Add(descriptor.Id);
        }

        foreach (InstalledExtension installed in loader.Store.Extensions)
        {
            loader.Admit(installed, compatible, hostApiVersion);
        }

        return loader;
    }

    /// <summary>Releases the load contexts, so a removed extension's file stops being held.</summary>
    public void UnloadAll()
    {
        foreach (string path in _loadedPaths) _engine.Unload(path);
        _loadedPaths.Clear();
        _plugins.Clear();
        _owners.Clear();
    }

    /// <summary>The installed extension a live plugin came from, or null when it came from plugins/.</summary>
    public string? OwnerOf(IPlugin plugin)
    {
        foreach (KeyValuePair<string, IPlugin> pair in _owners)
        {
            if (ReferenceEquals(pair.Value, plugin)) return pair.Key;
        }

        return null;
    }

    private void Admit(InstalledExtension installed, ICollection<string> compatible, string hostApiVersion)
    {
        if (!installed.Enabled)
        {
            _skipped.Add(new KeyValuePair<string, string>(installed.Id, "disabled by the operator"));
            return;
        }

        if (!compatible.Contains(installed.Id))
        {
            string reason = $"requires host API {installed.MinApiVersion}; this host is {hostApiVersion}";
            Store.RecordLoadFailure(installed.Id, reason);
            _skipped.Add(new KeyValuePair<string, string>(installed.Id, reason));
            return;
        }

        string path = Store.AssemblyPathFor(installed);
        if (!File.Exists(path))
        {
            string reason = $"entry assembly '{installed.EntryAssembly}' is missing from the store";
            Store.RecordLoadFailure(installed.Id, reason);
            _skipped.Add(new KeyValuePair<string, string>(installed.Id, reason));
            return;
        }

        if (!_engine.TryLoadAssemblyWithRetry(path, maxRetries: 0, delayMs: 0))
        {
            Store.RecordLoadFailure(installed.Id, _engine.LastError);
            _skipped.Add(new KeyValuePair<string, string>(installed.Id, _engine.LastError));
            return;
        }

        _loadedPaths.Add(path);
        foreach (IPlugin plugin in _engine.LastLoadedPlugins)
        {
            _plugins.Add(plugin);
            _owners[installed.Id] = plugin;
        }
    }

    /// <summary>Projects a store record onto the descriptor the registry and catalogue speak.</summary>
    private static ExtensionDescriptor Describe(InstalledExtension installed) => new()
    {
        Id = installed.Id,
        Name = installed.Name,
        Version = installed.Version,
        MinApiVersion = installed.MinApiVersion
    };
}
