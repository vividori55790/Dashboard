using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// Loads a compiled .NET plugin assembly and instantiates the <see cref="IPlugin"/> types it exports.
/// </summary>
/// <remarks>
/// Each assembly gets its own collectible <see cref="AssemblyLoadContext"/> and is loaded from a copy
/// of its bytes, matching <c>ManagedAssemblyScriptEngine</c>: loading through the file keeps the DLL
/// locked for the lifetime of the process, which makes replacing it — the entire point of hot-reload —
/// impossible after the first load.
/// <para>
/// Load failures are <em>not</em> swallowed here. A caller that asked for one specific plugin needs to
/// know why it did not appear, and <see cref="BadImageFormatException"/> in particular distinguishes a
/// corrupt or non-managed file from a plugin that merely exports nothing. <see cref="HotReloadEngine"/>
/// is the layer that turns those exceptions into a skipped file.
/// </para>
/// <para>
/// Plugin assemblies must resolve their dependencies against what the host has already loaded; no
/// private probing path is configured, so a plugin shipping its own third-party DLLs is out of scope.
/// </para>
/// </remarks>
public sealed class AssemblyPluginAdapter
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AssemblyLoadContext> _contexts =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Paths of the assemblies currently held in a load context.</summary>
    public IReadOnlyCollection<string> LoadedAssemblies
    {
        get { lock (_gate) { return new List<string>(_contexts.Keys); } }
    }

    /// <summary>
    /// Loads <paramref name="assemblyPath"/> and returns one instance of every exported
    /// <see cref="IPlugin"/> implementation it contains.
    /// </summary>
    /// <exception cref="ArgumentException">The path is empty.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="BadImageFormatException">The file is not a managed assembly.</exception>
    /// <exception cref="IOException">The file could not be read, typically because it is locked.</exception>
    public IReadOnlyList<IPlugin> LoadPlugin(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            throw new ArgumentException("A plugin assembly path is required.", nameof(assemblyPath));
        }

        // Read before creating the context so an unreadable file leaves nothing to unload.
        byte[] image = File.ReadAllBytes(assemblyPath);

        Unload(assemblyPath);

        var context = new AssemblyLoadContext($"plugin:{Path.GetFileName(assemblyPath)}", isCollectible: true);
        try
        {
            using var stream = new MemoryStream(image);
            Assembly assembly = context.LoadFromStream(stream);

            List<IPlugin> plugins = Instantiate(assembly);

            lock (_gate)
            {
                _contexts[assemblyPath] = context;
            }

            return plugins;
        }
        catch
        {
            // A half-loaded context would keep the failed assembly resident for no benefit.
            context.Unload();
            throw;
        }
    }

    /// <summary>
    /// Releases the load context for <paramref name="assemblyPath"/>.
    /// </summary>
    /// <returns><c>false</c> when nothing was loaded from that path.</returns>
    /// <remarks>
    /// Unloading is a request, not a guarantee: the runtime only reclaims the context once no
    /// reference into it survives, so callers must drop their <see cref="IPlugin"/> instances first.
    /// </remarks>
    public bool Unload(string assemblyPath)
    {
        AssemblyLoadContext? context;
        lock (_gate)
        {
            if (!_contexts.Remove(assemblyPath, out context)) return false;
        }

        context.Unload();
        return true;
    }

    /// <summary>
    /// Creates one instance per exported plugin type.
    /// </summary>
    /// <remarks>
    /// A type whose constructor throws is skipped rather than failing the whole assembly: plugin
    /// packs commonly bundle several plugins, and one broken entry should not cost the operator the
    /// working ones.
    /// </remarks>
    private static List<IPlugin> Instantiate(Assembly assembly)
    {
        var plugins = new List<IPlugin>();

        foreach (Type type in assembly.GetExportedTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;
            if (!typeof(IPlugin).IsAssignableFrom(type)) continue;
            if (type.GetConstructor(Type.EmptyTypes) is null) continue;

            try
            {
                if (Activator.CreateInstance(type) is IPlugin plugin) plugins.Add(plugin);
            }
            catch (Exception ex) when (ex is TargetInvocationException or MemberAccessException or MissingMethodException)
            {
                // Constructor faulted; the remaining types in the assembly are still usable.
            }
        }

        return plugins;
    }
}
