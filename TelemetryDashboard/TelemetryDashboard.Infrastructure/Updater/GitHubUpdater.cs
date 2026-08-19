using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using TelemetryDashboard.Infrastructure.WebServer;

namespace TelemetryDashboard.Infrastructure.Updater;

/// <summary>
/// Checks GitHub Releases for a newer build and verifies the downloaded asset.
/// </summary>
/// <remarks>
/// An unreachable endpoint yields an <c>Offline</c> result rather than an exception, so a machine
/// on an isolated plant network simply reports that it could not check. An asset is only ever
/// applied after its SHA-256 matches the published hash — an update channel that skips
/// verification is a remote code execution path.
/// </remarks>
public sealed class GitHubUpdater
{
    private readonly HttpClient _http;

    public GitHubUpdater(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd("TelemetryDashboard-Updater"))
        {
            // A missing user agent only affects GitHub's rate limiting, not correctness.
        }
    }

    /// <summary>Attempts per feed query, including the first.</summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>Injected wait between attempts; tests substitute a no-op to assert retries.</summary>
    public Func<TimeSpan, CancellationToken, Task>? RetryDelay { get; init; }

    public string CurrentVersion { get; private set; } = "0.0.0";

    public void SetCurrentVersion(string version) => CurrentVersion = version ?? "0.0.0";

    /// <summary>
    /// Queries a releases endpoint. Accepts a full API URL or an <c>owner/repo</c> shorthand.
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(string repositoryOrUrl, CancellationToken cancellationToken = default)
    {
        string url = ResolveReleasesUrl(repositoryOrUrl);

        try
        {
            // GitHub rate-limits anonymous callers hard, and answers with 403/429 rather than an
            // exception. A plant that checks for updates on a schedule will meet it.
            using HttpResponseMessage response = await HttpRetryExecutor.SendAsync(
                token => _http.GetAsync(url, token),
                MaxAttempts,
                delayAsync: RetryDelay,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = CurrentVersion,
                    StatusMessage = $"Release feed returned {(int)response.StatusCode}; no update applied."
                };
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(body);

            string latest = document.RootElement.TryGetProperty("tag_name", out JsonElement tag)
                ? tag.GetString() ?? string.Empty
                : string.Empty;

            string? asset = document.RootElement.TryGetProperty("assets", out JsonElement assets)
                            && assets.ValueKind == JsonValueKind.Array
                            && assets.GetArrayLength() > 0
                            && assets[0].TryGetProperty("browser_download_url", out JsonElement dl)
                ? dl.GetString()
                : null;

            UpdateCheckResult comparison = UpdateVersionComparer.Compare(CurrentVersion, latest);
            return new UpdateCheckResult
            {
                IsUpdateAvailable = comparison.IsUpdateAvailable,
                LatestVersion = latest,
                CurrentVersion = CurrentVersion,
                StatusMessage = comparison.StatusMessage,
                DownloadUrl = asset
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or UriFormatException or InvalidOperationException)
        {
            return new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                CurrentVersion = CurrentVersion,
                StatusMessage = $"Offline: could not reach the release feed ({ex.GetType().Name})."
            };
        }
    }

    /// <summary>Compares a candidate version against <see cref="CurrentVersion"/>.</summary>
    public Task<UpdateCheckResult> EvaluateVersionMatch(string candidateVersion) =>
        Task.FromResult(UpdateVersionComparer.Compare(CurrentVersion, candidateVersion));

    /// <summary>Verifies a downloaded asset against its published SHA-256.</summary>
    public bool VerifySha256(string filePath, string expectedHash) =>
        UpdateAssetVerifier.MatchesSha256(filePath, expectedHash);

    /// <summary>Starts the out-of-process patcher. Returns false when it is missing or blocked.</summary>
    public bool LaunchExternalPatcher(string patcherPath)
    {
        if (string.IsNullOrWhiteSpace(patcherPath) || !File.Exists(patcherPath)) return false;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = patcherPath,
                UseShellExecute = true
            };
            return Process.Start(startInfo) is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static string ResolveReleasesUrl(string repositoryOrUrl)
    {
        string input = (repositoryOrUrl ?? string.Empty).Trim();

        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return input;
        }

        return $"https://api.github.com/repos/{input}/releases/latest";
    }
}
