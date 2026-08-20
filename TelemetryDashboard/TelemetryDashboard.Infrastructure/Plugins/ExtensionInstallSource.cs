using System;
using System.IO;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// Where one install is reading from: the manifest text, the directory holding the payload, and the
/// hash the publisher committed to.
/// </summary>
/// <remarks>
/// Resolving the source is kept apart from verifying it so the two failure kinds stay separable. "I
/// cannot find a manifest for this file" is a packaging mistake the operator fixes in seconds;
/// "this manifest is not trustworthy" is a decision about someone else's code. Collapsing them into
/// one message would make the second look like the first.
/// </remarks>
public sealed class ExtensionInstallSource
{
    private ExtensionInstallSource(string manifestJson, string manifestOrigin, string packageDirectory, string origin, string? expectedSha256)
    {
        ManifestJson = manifestJson;
        ManifestOrigin = manifestOrigin;
        PackageDirectory = packageDirectory;
        Origin = origin;
        ExpectedSha256 = expectedSha256;
    }

    /// <summary>Raw manifest text, to be parsed by the verifier.</summary>
    public string ManifestJson { get; }

    /// <summary>Human description of where the manifest came from, for the refusal message.</summary>
    public string ManifestOrigin { get; }

    /// <summary>Directory the entry assembly is read from.</summary>
    public string PackageDirectory { get; }

    /// <summary>What is recorded in the store as the provenance of this extension.</summary>
    public string Origin { get; }

    /// <summary>Hash the catalogue published, or null for a local install with no published hash.</summary>
    public string? ExpectedSha256 { get; }

    /// <summary>
    /// Resolves a local install: a directory holding <c>extension.json</c>, or a <c>.dll</c> with a
    /// manifest beside it.
    /// </summary>
    /// <remarks>
    /// Two manifest names are accepted beside a DLL — <c>extension.json</c>, and
    /// <c>&lt;assembly&gt;.extension.json</c> — because a build that drops several plugin assemblies
    /// into one output folder cannot give them all the same manifest file name.
    /// </remarks>
    public static bool TryResolveLocal(string path, out ExtensionInstallSource? source, out ExtensionInstallOutcome? refusal)
    {
        source = null;
        refusal = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            refusal = ExtensionInstallOutcome.Refused("no extension path was given.");
            return false;
        }

        string full = Path.GetFullPath(path.Trim());

        if (Directory.Exists(full))
        {
            return TryRead(Path.Combine(full, ExtensionPackageManifest.FileName), full, out source, out refusal);
        }

        if (!File.Exists(full))
        {
            refusal = ExtensionInstallOutcome.Refused($"'{full}' is neither a file nor a directory.");
            return false;
        }

        if (!full.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            refusal = ExtensionInstallOutcome.Refused(
                $"'{Path.GetFileName(full)}' is not a .dll; install a plugin assembly or the "
                + "directory holding it.");
            return false;
        }

        string directory = Path.GetDirectoryName(full) ?? full;
        string sidecar = Path.Combine(directory, Path.GetFileNameWithoutExtension(full) + ".extension.json");
        string shared = Path.Combine(directory, ExtensionPackageManifest.FileName);

        return TryRead(File.Exists(sidecar) ? sidecar : shared, directory, out source, out refusal);
    }

    /// <summary>Builds a source from one entry of a catalogue index.</summary>
    /// <param name="entryJson">The raw manifest object as it appears in the index.</param>
    /// <param name="indexPath">The index file, used to resolve the entry's package directory.</param>
    /// <param name="packageDirectory">Directory the entry's payload was resolved to.</param>
    /// <param name="expectedSha256">Hash published in the index, or null.</param>
    public static ExtensionInstallSource FromCatalogue(
        string entryJson, string indexPath, string packageDirectory, string? expectedSha256) =>
        new(entryJson, $"entry in {Path.GetFileName(indexPath)}", packageDirectory,
            $"catalogue {indexPath}", expectedSha256);

    private static bool TryRead(
        string manifestPath, string packageDirectory,
        out ExtensionInstallSource? source, out ExtensionInstallOutcome? refusal)
    {
        source = null;
        refusal = null;

        if (!File.Exists(manifestPath))
        {
            refusal = ExtensionInstallOutcome.Refused(
                $"no {ExtensionPackageManifest.FileName} was found in '{packageDirectory}'. An "
                + "extension must describe itself before the host will run it.");
            return false;
        }

        try
        {
            source = new ExtensionInstallSource(
                File.ReadAllText(manifestPath), $"'{manifestPath}'", packageDirectory,
                packageDirectory, expectedSha256: null);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            refusal = ExtensionInstallOutcome.Refused($"'{manifestPath}' could not be read: {ex.Message}");
            return false;
        }
    }
}
