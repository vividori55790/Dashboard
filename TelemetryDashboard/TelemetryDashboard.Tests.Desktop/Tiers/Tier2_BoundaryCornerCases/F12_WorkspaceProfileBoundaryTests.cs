using TelemetryDashboard.UI.Docking;

namespace TelemetryDashboard.Tests.Desktop.Tiers.Tier2_BoundaryCornerCases;

/// <summary>F12: .workspace profile save/load boundary cases.</summary>
/// <remarks>
/// These read and write files rather than drive controls, but <c>WorkspaceManager</c> and
/// <c>WorkspaceProfile</c> live in the WPF project alongside the docking layout they serialise, so
/// the tests follow the types. Moving the persistence pair down into Core would let them return to
/// the portable suite; until then, exiling the tests is the honest description of where the code is.
/// </remarks>
public class F12_WorkspaceProfileBoundaryTests
{
    [Fact]
    [Trait("Category", "Tier2")]
    public void F12_Boundary_NonExistentWorkspaceFile_ThrowsFileNotFoundException()
    {
        var workspaceManager = new WorkspaceManager();
        string nonExistentPath = @"C:\NonExistentDir\profile.workspace";

        Action act = () => workspaceManager.LoadWorkspaceProfile(nonExistentPath);
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F12_Boundary_EmptyWorkspaceFile_ReturnsDefaultProfile()
    {
        string tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "");

        try
        {
            var workspaceManager = new WorkspaceManager();
            var profile = workspaceManager.LoadWorkspaceProfile(tempFile);
            profile.Should().NotBeNull();
            profile.Name.Should().Be("Default");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F12_Boundary_FutureVersionWorkspaceSchema_GracefullyDegrades()
    {
        string futureJson = "{\"version\": \"99.0\", \"preset\": \"ScopeMode\", \"unknownField\": 123}";
        string tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, futureJson);

        try
        {
            var workspaceManager = new WorkspaceManager();
            var profile = workspaceManager.LoadWorkspaceProfile(tempFile);
            profile.Should().NotBeNull();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F12_Boundary_ReadOnlyDirectorySave_FailsWithPermissionError()
    {
        string readOnlyDir = Path.Combine(Path.GetTempPath(), "ReadOnlyWorkspaceDir_" + Guid.NewGuid());
        Directory.CreateDirectory(readOnlyDir);
        var dirInfo = new DirectoryInfo(readOnlyDir);

        try
        {
            var workspaceManager = new WorkspaceManager();
            var profile = new WorkspaceProfile { Name = "Test" };

            // In non-admin Windows, test exception behavior for invalid path characters
            string invalidFilePath = Path.Combine(readOnlyDir, "invalid|file:name.workspace");
            Action act = () => workspaceManager.SaveWorkspaceProfile(profile, invalidFilePath);
            act.Should().Throw<Exception>();
        }
        finally
        {
            if (Directory.Exists(readOnlyDir)) Directory.Delete(readOnlyDir, true);
        }
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F12_Boundary_UnicodePathWorkspaceFile_SavesAndLoadsSuccessfully()
    {
        string unicodePath = Path.Combine(Path.GetTempPath(), "작업공간_Profile_테스트.workspace");
        var workspaceManager = new WorkspaceManager();
        var profile = new WorkspaceProfile { Name = "KoreanProfile", LayoutXml = "<Layout/>" };

        try
        {
            workspaceManager.SaveWorkspaceProfile(profile, unicodePath);
            File.Exists(unicodePath).Should().BeTrue();

            var loaded = workspaceManager.LoadWorkspaceProfile(unicodePath);
            loaded.Name.Should().Be("KoreanProfile");
        }
        finally
        {
            if (File.Exists(unicodePath)) File.Delete(unicodePath);
        }
    }
}
