using System;

namespace TelemetryDashboard.Infrastructure.Updater;

/// <summary>Compares a published release tag against the version currently running.</summary>
/// <remarks>
/// Split out of <see cref="GitHubUpdater"/> because the comparison is pure: it needs no release
/// feed, no network and no clock, so it can be reasoned about on its own. A tag that does not
/// parse yields "not comparable" rather than a guess — treating an unparseable tag as newer would
/// hand the release feed the decision to install.
/// </remarks>
internal static class UpdateVersionComparer
{
    /// <summary>Compares <paramref name="candidateVersion"/> against <paramref name="currentVersion"/>.</summary>
    internal static UpdateCheckResult Compare(string currentVersion, string candidateVersion)
    {
        bool candidateParsed = Version.TryParse(Normalize(candidateVersion), out Version? candidate);
        bool currentParsed = Version.TryParse(Normalize(currentVersion), out Version? current);

        if (!candidateParsed || !currentParsed)
        {
            return new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                LatestVersion = candidateVersion ?? string.Empty,
                CurrentVersion = currentVersion,
                StatusMessage = "Version strings are not comparable; no update applied."
            };
        }

        bool newer = candidate! > current!;
        return new UpdateCheckResult
        {
            IsUpdateAvailable = newer,
            LatestVersion = candidateVersion ?? string.Empty,
            CurrentVersion = currentVersion,
            StatusMessage = newer
                ? $"Update available: {currentVersion} -> {candidateVersion}"
                : $"Already up to date ({currentVersion})."
        };
    }

    /// <summary>Release tags are conventionally <c>v</c>-prefixed; <see cref="Version"/> is not.</summary>
    private static string Normalize(string? version)
    {
        string text = (version ?? string.Empty).Trim();
        return text.StartsWith('v') || text.StartsWith('V') ? text[1..] : text;
    }
}
