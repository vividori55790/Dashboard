namespace TelemetryDashboard.Infrastructure.Updater;

/// <summary>Outcome of an update check.</summary>
public sealed class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public string CurrentVersion { get; init; } = string.Empty;
    public string? DownloadUrl { get; init; }
}
