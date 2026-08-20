using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// Everything checked before an extension's bytes are allowed into the store.
/// </summary>
/// <remarks>
/// The order matters and is cheapest-first only where that is also safest: the manifest is read
/// before the file is touched, the hash is computed before the assembly is executed, and the
/// assembly is loaded into a collectible context that is unloaded again before the caller copies
/// anything. Nothing here trusts the publisher's word about what the package contains.
/// <para>
/// Loading the candidate is the one check that cannot be done by inspection, and it is the one that
/// matters most: a DLL that is not a managed assembly, or that exports no <c>IPlugin</c>, installs
/// perfectly and then does nothing at every subsequent start. That is precisely the "silently
/// skipped extension" this work exists to eliminate, so it is caught at install time where a person
/// is present to read the reason.
/// </para>
/// </remarks>
public static class ExtensionVerification
{
    /// <summary>Reads and validates the manifest text.</summary>
    public static bool TryReadManifest(
        string manifestJson,
        string manifestOrigin,
        out ExtensionPackageManifest? manifest,
        out ExtensionInstallOutcome? refusal)
    {
        refusal = null;

        if (!ExtensionPackageManifest.TryRead(manifestJson, out manifest, out string failure))
        {
            refusal = ExtensionInstallOutcome.Refused($"manifest {manifestOrigin} rejected: {failure}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Confirms the assembly is present and its bytes hash to what was published.
    /// </summary>
    /// <param name="expectedSha256">
    /// The hash the catalogue or manifest published, or null when none was. A missing hash is not
    /// treated as a match: the outcome records what the bytes actually hashed to, so the store can
    /// hold a fingerprint of what was accepted even for an unsigned package.
    /// </param>
    public static bool TryHashAssembly(
        string assemblyPath,
        string? expectedSha256,
        string extensionId,
        out string actualSha256,
        out ExtensionInstallOutcome? refusal)
    {
        actualSha256 = string.Empty;
        refusal = null;

        if (!File.Exists(assemblyPath))
        {
            refusal = ExtensionInstallOutcome.Refused(
                $"'{extensionId}' names entry assembly '{Path.GetFileName(assemblyPath)}', which is "
                + $"not in the package at {Path.GetDirectoryName(assemblyPath)}.", extensionId);
            return false;
        }

        try
        {
            using FileStream stream = File.OpenRead(assemblyPath);
            actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            refusal = ExtensionInstallOutcome.Refused(
                $"'{extensionId}' could not be read: {ex.Message}", extensionId);
            return false;
        }

        string? expected = Normalise(expectedSha256);
        if (expected is not null && !string.Equals(expected, actualSha256, StringComparison.Ordinal))
        {
            refusal = ExtensionInstallOutcome.Refused(
                $"'{extensionId}' failed its integrity check: the catalogue published sha256 "
                + $"{expected}, the file hashes to {actualSha256}.", extensionId);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Loads the candidate in a collectible context, requires at least one plugin, and unloads.
    /// </summary>
    /// <remarks>
    /// The context is released before returning so the caller can copy over, or delete, the file it
    /// just probed. <see cref="AssemblyPluginAdapter"/> loads from a byte copy rather than the path
    /// for the same reason, so the probe never leaves the candidate locked.
    /// </remarks>
    public static bool TryLoadCandidate(
        string assemblyPath,
        string extensionId,
        out int pluginCount,
        out ExtensionInstallOutcome? refusal)
    {
        pluginCount = 0;
        refusal = null;

        var adapter = new AssemblyPluginAdapter();
        try
        {
            IReadOnlyList<IPlugin> plugins = adapter.LoadPlugin(assemblyPath);
            pluginCount = plugins.Count;

            if (pluginCount == 0)
            {
                refusal = ExtensionInstallOutcome.Refused(
                    $"'{extensionId}' loaded, but exports no public IPlugin with a parameterless "
                    + "constructor, so nothing would ever run.", extensionId);
                return false;
            }
        }
        catch (Exception ex)
        {
            refusal = ExtensionInstallOutcome.Refused(
                $"'{extensionId}' entry assembly '{Path.GetFileName(assemblyPath)}' will not load: "
                + $"{ex.GetType().Name}: {ex.Message}", extensionId);
            return false;
        }
        finally
        {
            adapter.Unload(assemblyPath);
        }

        return true;
    }

    /// <summary>Accepts a hash with or without separators and in any casing, or null when absent.</summary>
    private static string? Normalise(string? sha) =>
        string.IsNullOrWhiteSpace(sha) ? null : sha.Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
}
