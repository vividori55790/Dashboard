using System;
using System.IO;
using System.Security.Cryptography;

namespace TelemetryDashboard.Infrastructure.Updater;

/// <summary>Hashes a downloaded update asset and compares it with its published SHA-256.</summary>
/// <remarks>
/// Split out of <see cref="GitHubUpdater"/> so the one check standing between a release feed and
/// code execution on this machine is a single self-contained function with no other job. Every
/// failure — missing file, missing hash, unreadable bytes — answers "does not match": when the
/// asset cannot be proven to be the published one, the only safe answer is no.
/// </remarks>
internal static class UpdateAssetVerifier
{
    /// <summary>True only when the file exists and hashes to <paramref name="expectedHash"/>.</summary>
    internal static bool MatchesSha256(string filePath, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
        if (string.IsNullOrWhiteSpace(expectedHash)) return false;

        try
        {
            using FileStream stream = File.OpenRead(filePath);
            byte[] actual = SHA256.HashData(stream);

            string normalized = expectedHash.Replace("-", string.Empty).Trim();
            return string.Equals(Convert.ToHexString(actual), normalized, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
