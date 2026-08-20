using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// The directory the host installs extensions into, and the state of everything in it.
/// </summary>
/// <remarks>
/// Separate from the <c>plugins/</c> drop folder on purpose. That folder is "load every DLL you
/// find", which cannot express a disabled extension or remember why one failed; this store owns a
/// directory per extension plus <see cref="ExtensionStateFile"/>, so an extension has an identity
/// the host can act on. Both paths stay live: dropping a DLL into <c>plugins/</c> still works, and
/// nothing an operator already relies on was taken away to add this.
/// <para>
/// Every mutation writes the state file before returning. A process that dies between changing
/// memory and persisting would otherwise report an extension as enabled until the next restart
/// silently reverted it.
/// </para>
/// </remarks>
public sealed class ExtensionStore
{
    private readonly ExtensionStateFile _state;
    private readonly List<InstalledExtension> _extensions;

    /// <param name="directory">Root of the store, created on first write rather than here.</param>
    public ExtensionStore(string directory)
    {
        _state = new ExtensionStateFile(directory);
        _extensions = _state.Read();
    }

    /// <summary>Root directory of the store.</summary>
    public string Directory => _state.Directory;

    /// <summary>Why the state file could not be read, or null. Extensions listed will be empty.</summary>
    public string? StateFailure => _state.ReadFailure;

    /// <summary>Everything installed, ordered by id so two runs list it the same way.</summary>
    public IReadOnlyList<InstalledExtension> Extensions =>
        _extensions.OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Looks one up by id, or null.</summary>
    public InstalledExtension? Find(string id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : _extensions.FirstOrDefault(e => string.Equals(e.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Directory holding one extension's files.</summary>
    public string DirectoryFor(string id) => Path.Combine(Directory, SafeFolder(id));

    /// <summary>Full path of the assembly the host would load for <paramref name="extension"/>.</summary>
    public string AssemblyPathFor(InstalledExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return Path.Combine(DirectoryFor(extension.Id), extension.EntryAssembly);
    }

    /// <summary>Records an installed extension, replacing any earlier record of the same id.</summary>
    /// <remarks>
    /// Replacement rather than first-writer-wins, because this is called only after an install has
    /// already verified and written the new files: refusing the record here would leave the store
    /// describing the assembly it no longer holds.
    /// </remarks>
    public void Upsert(InstalledExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);

        _extensions.RemoveAll(e => string.Equals(e.Id, extension.Id, StringComparison.OrdinalIgnoreCase));
        _extensions.Add(extension);
        _state.Write(_extensions);
    }

    /// <summary>Turns one extension on or off. Returns false when nothing carries that id.</summary>
    public bool SetEnabled(string id, bool enabled)
    {
        InstalledExtension? found = Find(id);
        if (found is null) return false;

        found.Enabled = enabled;
        _state.Write(_extensions);
        return true;
    }

    /// <summary>
    /// Deletes an extension's files and forgets it.
    /// </summary>
    /// <param name="id">Extension to remove.</param>
    /// <param name="failure">Why the files could not be deleted, or empty.</param>
    /// <returns><c>false</c> when the id was unknown or the files survived.</returns>
    /// <remarks>
    /// The record is dropped only after the directory is gone. A removal that forgot the extension
    /// while its DLL was still on disk would leave a file the next install cannot overwrite and
    /// nothing left to name it by — the silent half-completion this store exists to avoid.
    /// <para>
    /// A host that is already running does not block this, and that was verified rather than
    /// assumed: <see cref="AssemblyPluginAdapter"/> loads a plugin from a byte copy into a
    /// collectible context, so the file on disk is never held open. The consequence is worth being
    /// exact about — the running host keeps executing the copy it loaded until it exits, and only
    /// then releases the context. Removal changes the next start, not this one.
    /// </para>
    /// <para>
    /// The failure path is therefore about ordinary I/O — an editor, a scanner, a permission — and
    /// it names the file that survived instead of reporting a success it did not achieve.
    /// </para>
    /// </remarks>
    public bool Remove(string id, out string failure)
    {
        failure = string.Empty;
        InstalledExtension? found = Find(id);
        if (found is null)
        {
            failure = $"no extension with id '{id}' is installed.";
            return false;
        }

        string folder = DirectoryFor(found.Id);
        try
        {
            if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failure = $"'{found.Id}' is still on disk: {ex.Message}";
            return false;
        }

        _extensions.Remove(found);
        _state.Write(_extensions);
        return true;
    }

    /// <summary>Records a load failure so the report can name it. Runtime only; never persisted.</summary>
    public void RecordLoadFailure(string id, string reason)
    {
        InstalledExtension? found = Find(id);
        if (found is not null) found.LoadFailure = reason;
    }

    /// <summary>Strips anything that could make an id escape the store directory.</summary>
    private static string SafeFolder(string id)
    {
        string trimmed = (id ?? string.Empty).Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars()) trimmed = trimmed.Replace(invalid, '_');
        return trimmed.Replace("..", "__", StringComparison.Ordinal);
    }
}
