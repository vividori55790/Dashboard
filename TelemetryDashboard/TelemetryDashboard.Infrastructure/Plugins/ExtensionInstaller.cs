using System;
using System.IO;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// Takes a verified extension package and puts it where the host will load it.
/// </summary>
/// <remarks>
/// Nothing is copied until every check in <see cref="ExtensionVerification"/> has passed, so a
/// refused package leaves the store byte-for-byte as it was. The alternative — copy first, validate
/// on the next start — is how a broken extension becomes something an operator has to clean up by
/// hand, and how a package that never loads gets recorded as installed.
/// <para>
/// Installing is only ever reached from an explicit <c>extensions install</c> command. Listing a
/// catalogue does not call it, and neither does starting the host: running a third party's code in
/// this process is a decision somebody makes, not a consequence of pointing at an index.
/// </para>
/// </remarks>
public sealed class ExtensionInstaller
{
    private readonly ExtensionStore _store;

    public ExtensionInstaller(ExtensionStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>Installs from a local directory or <c>.dll</c> path.</summary>
    public ExtensionInstallOutcome InstallFromPath(string path)
    {
        if (!ExtensionInstallSource.TryResolveLocal(path, out ExtensionInstallSource? source, out ExtensionInstallOutcome? refusal))
        {
            return refusal!;
        }

        return Install(source!);
    }

    /// <summary>Verifies a resolved source and, only then, writes it into the store.</summary>
    public ExtensionInstallOutcome Install(ExtensionInstallSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!ExtensionVerification.TryReadManifest(
                source.ManifestJson, source.ManifestOrigin,
                out ExtensionPackageManifest? manifest, out ExtensionInstallOutcome? refusal))
        {
            return refusal!;
        }

        string id = manifest!.Descriptor.Id.Trim();
        string assemblyPath = Path.Combine(source.PackageDirectory, manifest.EntryAssembly);

        if (!ExtensionVerification.TryHashAssembly(
                assemblyPath, source.ExpectedSha256 ?? manifest.Sha256, id,
                out string sha256, out refusal))
        {
            return refusal!;
        }

        if (!ExtensionVerification.TryLoadCandidate(assemblyPath, id, out int pluginCount, out refusal))
        {
            return refusal!;
        }

        try
        {
            return Store(source, manifest, assemblyPath, sha256, pluginCount);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ExtensionInstallOutcome.Refused($"'{id}' verified but could not be stored: {ex.Message}", id);
        }
    }

    /// <summary>
    /// Copies the payload and records it, preserving an existing enable/disable decision.
    /// </summary>
    /// <remarks>
    /// Re-installing must not silently switch a disabled extension back on. An operator who turned
    /// one off did so for a reason, and an upgrade is not a retraction of it.
    /// </remarks>
    private ExtensionInstallOutcome Store(
        ExtensionInstallSource source, ExtensionPackageManifest manifest,
        string assemblyPath, string sha256, int pluginCount)
    {
        string id = manifest.Descriptor.Id.Trim();
        InstalledExtension? existing = _store.Find(id);
        string target = _store.DirectoryFor(id);

        if (string.Equals(Path.GetFullPath(source.PackageDirectory), target, StringComparison.OrdinalIgnoreCase))
        {
            return ExtensionInstallOutcome.Refused(
                $"'{id}' is already installed at that exact path; installing it onto itself would "
                + "delete the payload it is reading.", id);
        }

        Directory.CreateDirectory(target);
        File.Copy(assemblyPath, Path.Combine(target, manifest.EntryAssembly), overwrite: true);
        File.WriteAllText(Path.Combine(target, ExtensionPackageManifest.FileName), source.ManifestJson);

        var installed = new InstalledExtension
        {
            Id = id,
            Name = manifest.Descriptor.Name,
            Version = manifest.Descriptor.Version,
            EntryAssembly = manifest.EntryAssembly,
            MinApiVersion = manifest.Descriptor.MinApiVersion,
            Sha256 = sha256,
            Origin = source.Origin,
            InstalledUtc = DateTime.UtcNow,
            Enabled = existing?.Enabled ?? true
        };

        _store.Upsert(installed);

        string verb = existing is null ? "installed" : "replaced";
        return ExtensionInstallOutcome.Accepted(
            installed,
            $"{verb} into {target} -- {pluginCount} plugin type(s) verified, sha256 {sha256[..16]}...");
    }
}
