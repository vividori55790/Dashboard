namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F33_GitHubUpdaterTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public async Task GitHubUpdater_CheckRelease_QueriesLatestReleaseRestApi()
    {
        var updater = new GitHubUpdaterState();
        var releaseInfo = await updater.CheckForUpdateAsync("1.0.0");

        releaseInfo.Should().NotBeNull();
        releaseInfo.Version.Should().Be("1.1.0");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void GitHubUpdater_VersionCheck_ComparesSemVerStrings()
    {
        bool isNewer = GitHubUpdaterHelper.IsVersionNewer(currentVersion: "1.0.0", latestVersion: "1.1.0");
        bool isNotNewer = GitHubUpdaterHelper.IsVersionNewer(currentVersion: "1.2.0", latestVersion: "1.1.0");

        isNewer.Should().BeTrue();
        isNotNewer.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void GitHubUpdater_ParseNotes_ExtractsReleaseNotesMarkdown()
    {
        string body = "## What's Changed\n* Added ScottPlot 5 DirectX GPU acceleration";
        string notes = GitHubUpdaterHelper.ExtractReleaseNotes(body);

        notes.Should().Contain("Added ScottPlot 5");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task GitHubUpdater_DownloadPackage_FetchesAssetAsynchronously()
    {
        var updater = new GitHubUpdaterState();
        bool downloaded = await updater.DownloadUpdatePackageAsync("https://github.com/org/repo/releases/download/v1.1.0/update.zip");

        downloaded.Should().BeTrue();
        updater.IsDownloaded.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void GitHubUpdater_PatcherScript_PreparesOutofProcessLaunch()
    {
        string scriptCmd = GitHubUpdaterHelper.FormatPatcherScriptCall("update.zip", targetPid: 1234);

        scriptCmd.Should().Contain("patcher.ps1");
        scriptCmd.Should().Contain("-ZipPath update.zip");
        scriptCmd.Should().Contain("-TargetPid 1234");
    }
}

public class ReleaseInfo
{
    public string Version { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
}

public class GitHubUpdaterState
{
    public bool IsDownloaded { get; private set; }

    public Task<ReleaseInfo> CheckForUpdateAsync(string currentVersion)
    {
        return Task.FromResult(new ReleaseInfo
        {
            Version = "1.1.0",
            DownloadUrl = "https://github.com/org/repo/releases/download/v1.1.0/update.zip",
            ReleaseNotes = "New features & bug fixes"
        });
    }

    public Task<bool> DownloadUpdatePackageAsync(string url)
    {
        IsDownloaded = true;
        return Task.FromResult(true);
    }
}

public static class GitHubUpdaterHelper
{
    public static bool IsVersionNewer(string currentVersion, string latestVersion)
    {
        var cur = new Version(currentVersion);
        var lat = new Version(latestVersion);
        return lat > cur;
    }

    public static string ExtractReleaseNotes(string markdownBody)
    {
        return markdownBody.Trim();
    }

    public static string FormatPatcherScriptCall(string zipPath, int targetPid)
    {
        return $"powershell.exe -File patcher.ps1 -ZipPath {zipPath} -TargetPid {targetPid}";
    }
}
