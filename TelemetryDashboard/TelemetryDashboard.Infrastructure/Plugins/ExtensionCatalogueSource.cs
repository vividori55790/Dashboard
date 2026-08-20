using System;
using System.Collections.Generic;
using System.IO;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// Finds one entry in a catalogue index and turns it into something installable.
/// </summary>
/// <remarks>
/// Only a catalogue on a filesystem — a local path or a network share — can be installed from.
/// <see cref="ManifestIndexMarketplace"/> happily <em>lists</em> an <c>http(s)</c> index, and that
/// asymmetry is deliberate: listing costs nothing if the index is wrong, while installing runs the
/// bytes it names inside this process. Fetching a payload over HTTP safely needs publisher
/// signatures this build does not implement, so the case is refused by name instead of being
/// approximated with a hash the same server could have rewritten.
/// <para>
/// The entry's own <c>sha256</c> is carried through as the expected hash. That is what makes a
/// catalogue worth pointing at: the index states what the payload should be, and a payload that
/// disagrees is refused rather than installed and reported later.
/// </para>
/// </remarks>
public static class ExtensionCatalogueSource
{
    /// <summary>Resolves <paramref name="extensionId"/> from the index at <paramref name="indexLocation"/>.</summary>
    /// <param name="indexLocation">Filesystem path of a JSON catalogue index.</param>
    /// <param name="extensionId">Id of the entry to install.</param>
    /// <param name="source">The resolved source, or null.</param>
    /// <param name="refusal">Why nothing could be resolved, or null.</param>
    public static bool TryResolve(
        string indexLocation, string extensionId,
        out ExtensionInstallSource? source, out ExtensionInstallOutcome? refusal)
    {
        source = null;
        refusal = null;

        if (indexLocation.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || indexLocation.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            refusal = ExtensionInstallOutcome.Refused(
                "installing from an http(s) catalogue is not supported by this build: the payload "
                + "would be executed on the word of the same server that published its hash. Point "
                + "--catalogue at a file or a network share.", extensionId);
            return false;
        }

        if (!File.Exists(indexLocation))
        {
            refusal = ExtensionInstallOutcome.Refused($"catalogue index '{indexLocation}' does not exist.", extensionId);
            return false;
        }

        string indexText;
        try
        {
            indexText = File.ReadAllText(indexLocation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            refusal = ExtensionInstallOutcome.Refused($"catalogue index could not be read: {ex.Message}", extensionId);
            return false;
        }

        return TryMatch(indexText, indexLocation, extensionId, out source, out refusal);
    }

    /// <summary>Scans the index for the wanted id, keeping each entry's raw text.</summary>
    /// <remarks>
    /// The raw text becomes the installed manifest verbatim, so what the store holds is exactly what
    /// the catalogue published — not a re-serialisation that could quietly drop a field this build
    /// does not understand yet.
    /// </remarks>
    private static bool TryMatch(
        string indexText, string indexLocation, string extensionId,
        out ExtensionInstallSource? source, out ExtensionInstallOutcome? refusal)
    {
        source = null;
        refusal = null;

        IReadOnlyList<string> entries = ManifestIndexSplitter.SplitTopLevelObjects(indexText);
        var listed = new List<string>();

        foreach (string entry in entries)
        {
            if (!ExtensionPackageManifest.TryRead(entry, out ExtensionPackageManifest? manifest, out _)
                || manifest is null)
            {
                continue;
            }

            listed.Add(manifest.Descriptor.Id);
            if (!string.Equals(manifest.Descriptor.Id, extensionId.Trim(), StringComparison.OrdinalIgnoreCase)) continue;

            string indexDirectory = Path.GetDirectoryName(Path.GetFullPath(indexLocation)) ?? ".";
            string packageDirectory = string.IsNullOrWhiteSpace(manifest.Package)
                ? indexDirectory
                : Path.GetFullPath(Path.Combine(indexDirectory, manifest.Package));

            source = ExtensionInstallSource.FromCatalogue(entry, indexLocation, packageDirectory, manifest.Sha256);
            return true;
        }

        // Naming what the catalogue does hold: the usual cause is a typo or an entry the parser
        // rejected, and an operator cannot tell those apart from "not found" alone.
        refusal = ExtensionInstallOutcome.Refused(
            $"'{extensionId}' is not an installable entry in {indexLocation}. Installable ids there: "
            + (listed.Count == 0 ? "(none)" : string.Join(", ", listed)) + ".", extensionId);
        return false;
    }
}
