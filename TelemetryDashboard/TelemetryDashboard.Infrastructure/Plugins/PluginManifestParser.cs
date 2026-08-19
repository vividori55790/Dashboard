using System;
using System.Text.Json;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// Reads an extension manifest document into an <see cref="ExtensionDescriptor"/>.
/// </summary>
/// <remarks>
/// Manifests arrive from the marketplace and from whatever a user drops into the plugins folder,
/// so a damaged or hostile document is an ordinary case rather than an exceptional one. Every
/// failure is reported through the return value: a parser that throws while enumerating a folder
/// would abort the scan and hide the plugins that follow the broken one.
/// <para>
/// A manifest without an id is rejected even when the JSON is well formed — the id is what
/// <see cref="ExtensionRegistry"/> deduplicates on, so an anonymous entry cannot be tracked,
/// updated, or removed later.
/// </para>
/// </remarks>
public sealed class PluginManifestParser
{
    /// <summary>Manifests are authored by hand, so casing is not worth failing a load over.</summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Attempts to read <paramref name="json"/> as a manifest.
    /// </summary>
    /// <param name="json">Raw manifest text.</param>
    /// <param name="descriptor">The parsed descriptor, or <c>null</c> when parsing failed.</param>
    /// <returns><c>true</c> only when a usable descriptor was produced.</returns>
    public bool TryParseManifest(string json, out ExtensionDescriptor? descriptor)
    {
        descriptor = null;
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            ExtensionDescriptor? parsed = JsonSerializer.Deserialize<ExtensionDescriptor>(json, Options);

            // "null" is valid JSON but describes no extension.
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Id)) return false;

            descriptor = parsed;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            // Truncated, mistyped, or non-object manifests all land here and are simply skipped.
            return false;
        }
    }
}
