using System;
using System.IO;
using System.Text.Json;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// An extension's <c>extension.json</c>: the catalogue metadata plus the two facts an installer
/// needs — which assembly to load, and what its bytes are supposed to hash to.
/// </summary>
/// <remarks>
/// <see cref="ExtensionDescriptor"/> already carries the descriptive half and is parsed here by
/// <see cref="PluginManifestParser"/> rather than re-implemented, so a manifest listed in a
/// catalogue and a manifest sitting beside a DLL are read by exactly the same code. This type adds
/// only what installing requires; the descriptor is left untouched because a catalogue listing must
/// keep working without an <c>entryAssembly</c> it will never load.
/// <para>
/// Every rejection returns a sentence naming the field at fault. "Manifest invalid" tells an
/// operator holding a third party's ZIP nothing they can act on.
/// </para>
/// </remarks>
public sealed class ExtensionPackageManifest
{
    /// <summary>Name the manifest carries inside an installed extension's directory.</summary>
    public const string FileName = "extension.json";

    private ExtensionPackageManifest(ExtensionDescriptor descriptor, string entryAssembly, string? sha256, string? package)
    {
        Descriptor = descriptor;
        EntryAssembly = entryAssembly;
        Sha256 = sha256;
        Package = package;
    }

    /// <summary>Id, name, version and minimum host API, shared with the catalogue listing.</summary>
    public ExtensionDescriptor Descriptor { get; }

    /// <summary>File name of the assembly holding the <c>IPlugin</c> implementations.</summary>
    public string EntryAssembly { get; }

    /// <summary>Expected SHA-256 of <see cref="EntryAssembly"/>, or null when the author published none.</summary>
    public string? Sha256 { get; }

    /// <summary>Where a catalogue entry's payload lives, relative to the index. Null for a local manifest.</summary>
    public string? Package { get; }

    /// <summary>Reads a manifest, reporting the specific field that made it unusable.</summary>
    /// <param name="json">Raw manifest text.</param>
    /// <param name="manifest">The parsed manifest, or null on failure.</param>
    /// <param name="failure">A sentence naming what was wrong, or empty on success.</param>
    public static bool TryRead(string json, out ExtensionPackageManifest? manifest, out string failure)
    {
        manifest = null;
        failure = string.Empty;

        var parser = new PluginManifestParser();
        if (!parser.TryParseManifest(json, out ExtensionDescriptor? descriptor) || descriptor is null)
        {
            failure = "the manifest is not valid JSON, or carries no 'id'.";
            return false;
        }

        string? entry = ReadString(json, "entryAssembly");
        if (string.IsNullOrWhiteSpace(entry))
        {
            failure = $"'{descriptor.Id}' names no 'entryAssembly', so there is nothing to load.";
            return false;
        }

        if (!IsBareFileName(entry))
        {
            failure = $"'{descriptor.Id}' has entryAssembly '{entry}': a bare file name is required, "
                + "not a path.";
            return false;
        }

        if (!entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            failure = $"'{descriptor.Id}' has entryAssembly '{entry}', which is not a .dll.";
            return false;
        }

        manifest = new ExtensionPackageManifest(
            descriptor, entry, ReadString(json, "sha256"), ReadString(json, "package"));
        return true;
    }

    /// <summary>Reads one optional string property, tolerating any casing the author used.</summary>
    /// <remarks>
    /// A second pass over the same text rather than a DTO with these fields on it: the descriptor
    /// belongs to Core and must not grow install-time concerns, and the cost of re-parsing a
    /// document measured in hundreds of bytes is not worth a duplicated model.
    /// </remarks>
    private static string? ReadString(string json, string propertyName)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });

            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) continue;
                return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            }
        }
        catch (JsonException)
        {
            // Unreachable in practice — the descriptor parse above already accepted this document —
            // but a null here degrades to "field absent", which the caller reports precisely.
        }

        return null;
    }

    /// <summary>
    /// True when the name cannot escape the extension's own directory.
    /// </summary>
    /// <remarks>
    /// The installer copies this file into a directory it owns and later loads it. A manifest
    /// saying <c>../../TelemetryDashboard.Core.dll</c> would make that copy overwrite the host, so
    /// the check happens before any byte is written rather than being trusted to the copy.
    /// </remarks>
    private static bool IsBareFileName(string entry) =>
        entry.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !entry.Contains("..", StringComparison.Ordinal)
        && string.Equals(Path.GetFileName(entry), entry, StringComparison.Ordinal);
}
