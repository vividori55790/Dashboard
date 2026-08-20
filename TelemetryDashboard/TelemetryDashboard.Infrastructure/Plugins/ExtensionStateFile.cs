using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// Reads and writes <c>installed.json</c>, the record of what is installed and what is switched on.
/// </summary>
/// <remarks>
/// State lives in a file rather than in the directory listing because "installed but disabled" has
/// no representation as a set of files: an extension whose assembly is present but must not load
/// is indistinguishable from one that should. Inferring state from the filesystem would mean
/// disabling had to move or rename files, and a half-completed move would lose the extension.
/// <para>
/// Writes go to a temporary file and are then moved over the target. A host killed mid-write would
/// otherwise leave a truncated document, and the next start would read it as "nothing is
/// installed" — silently un-installing every extension the operator had added.
/// </para>
/// </remarks>
public sealed class ExtensionStateFile
{
    /// <summary>Name of the state document inside the extension directory.</summary>
    public const string FileName = "installed.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        AllowTrailingCommas = true
    };

    private readonly string _path;

    /// <param name="directory">The extension store's root directory.</param>
    public ExtensionStateFile(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("An extension directory is required.", nameof(directory));
        }

        Directory = System.IO.Path.GetFullPath(directory);
        _path = System.IO.Path.Combine(Directory, FileName);
    }

    /// <summary>Root directory the state file lives in.</summary>
    public string Directory { get; }

    /// <summary>Full path of the state document, whether or not it exists yet.</summary>
    /// <remarks>Named FilePath, not Path: a property called Path would shadow <see cref="System.IO.Path"/>.</remarks>
    public string FilePath => _path;

    /// <summary>Why the state file could not be read, or null when the last read was clean.</summary>
    /// <remarks>
    /// A damaged state file is reported rather than silently replaced. Overwriting it would erase
    /// the operator's enable/disable decisions and leave no trace of having done so.
    /// </remarks>
    public string? ReadFailure { get; private set; }

    /// <summary>Loads the recorded extensions. An absent file is an empty list, not a failure.</summary>
    public List<InstalledExtension> Read()
    {
        ReadFailure = null;
        if (!File.Exists(_path)) return new List<InstalledExtension>();

        try
        {
            List<InstalledExtension>? loaded =
                JsonSerializer.Deserialize<List<InstalledExtension>>(File.ReadAllText(_path), Options);

            return loaded ?? new List<InstalledExtension>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            ReadFailure = $"{System.IO.Path.GetFileName(_path)} could not be read: {ex.Message}";
            return new List<InstalledExtension>();
        }
    }

    /// <summary>Writes the list, replacing whatever was there.</summary>
    /// <exception cref="IOException">The state could not be persisted.</exception>
    public void Write(IReadOnlyList<InstalledExtension> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        System.IO.Directory.CreateDirectory(Directory);

        string temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(extensions, Options));
        File.Move(temporary, _path, overwrite: true);
    }
}
