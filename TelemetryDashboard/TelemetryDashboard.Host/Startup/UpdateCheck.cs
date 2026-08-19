using System;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Infrastructure.Updater;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Asks the release feed whether a newer build exists, and says so. Applies nothing.
/// </summary>
/// <remarks>
/// The updater underneath verifies a downloaded asset against its published SHA-256 and can launch
/// an out-of-process patcher, and until now nothing constructed it, so neither capability existed
/// in any running program. This wires the half that is safe to do unattended. Downloading and
/// applying stays a decision an operator makes, because a hub that updates itself on a plant
/// network is an execution path from the internet into the plant.
/// </remarks>
public static class UpdateCheck
{
    /// <summary>Checks the configured feed and prints the outcome. Silent when unconfigured.</summary>
    public static async Task PrintAsync(HostOptions options, string currentVersion, CancellationToken cancellationToken)
    {
        if (options.UpdateRepository is null) return;

        var updater = new GitHubUpdater();
        updater.SetCurrentVersion(currentVersion);

        UpdateCheckResult result = await updater
            .CheckForUpdatesAsync(options.UpdateRepository, cancellationToken)
            .ConfigureAwait(false);

        foreach (string line in Render(options.UpdateRepository, result))
        {
            Console.WriteLine(line);
        }
    }

    /// <summary>Builds the report lines, so their wording can be asserted without a network.</summary>
    public static string[] Render(string repository, UpdateCheckResult result)
    {
        if (result.IsUpdateAvailable)
        {
            return new[]
            {
                $"  updates       {result.LatestVersion} available (running {result.CurrentVersion})",
                $"                {repository}",
                "                Nothing was downloaded or applied."
            };
        }

        return new[]
        {
            $"  updates       {result.StatusMessage}",
            $"                {repository}"
        };
    }
}
