using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// In-memory catalogue of the extensions currently known to the host, keyed by extension id.
/// </summary>
/// <remarks>
/// Registration is first-writer-wins: a second manifest claiming an id that is already present is
/// rejected rather than replacing the incumbent. Extensions are discovered from several sources at
/// once (marketplace catalogue, plugins folder scan, hot-reload watcher), so duplicates are routine;
/// letting a late arrival silently overwrite an already-initialised extension would swap the
/// descriptor out from under a live instance.
/// <para>
/// The folder watcher raises its events on a thread-pool thread while the UI enumerates the
/// catalogue, so every operation takes the lock and enumeration returns a snapshot.
/// </para>
/// </remarks>
public sealed class ExtensionRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ExtensionDescriptor> _extensions =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Number of registered extensions.</summary>
    public int Count
    {
        get { lock (_gate) { return _extensions.Count; } }
    }

    /// <summary>
    /// Adds <paramref name="descriptor"/> to the catalogue.
    /// </summary>
    /// <returns>
    /// <c>false</c> when the descriptor is unusable or its id is already registered, in which case
    /// the catalogue is left untouched.
    /// </returns>
    public bool Register(ExtensionDescriptor descriptor)
    {
        if (descriptor is null || string.IsNullOrWhiteSpace(descriptor.Id)) return false;

        lock (_gate)
        {
            return _extensions.TryAdd(descriptor.Id.Trim(), descriptor);
        }
    }

    /// <summary>Removes an extension by id. Returns <c>false</c> when it was not registered.</summary>
    public bool Unregister(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId)) return false;

        lock (_gate)
        {
            return _extensions.Remove(extensionId.Trim());
        }
    }

    /// <summary>Looks up a single extension by id.</summary>
    public ExtensionDescriptor? Find(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId)) return null;

        lock (_gate)
        {
            return _extensions.TryGetValue(extensionId.Trim(), out ExtensionDescriptor? found) ? found : null;
        }
    }

    /// <summary>
    /// Returns a snapshot of the catalogue, ordered by name so the store list is stable between
    /// refreshes rather than following dictionary insertion order.
    /// </summary>
    public IReadOnlyList<ExtensionDescriptor> GetExtensions()
    {
        lock (_gate)
        {
            return _extensions.Values
                .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    /// <summary>Returns the extensions a host on <paramref name="hostApiVersion"/> can actually load.</summary>
    public IReadOnlyList<ExtensionDescriptor> GetCompatibleExtensions(string hostApiVersion)
    {
        return GetExtensions()
            .Where(e => e.IsCompatibleWithApiVersion(hostApiVersion))
            .ToList();
    }

    /// <summary>Empties the catalogue, for a full rescan.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _extensions.Clear();
        }
    }
}
