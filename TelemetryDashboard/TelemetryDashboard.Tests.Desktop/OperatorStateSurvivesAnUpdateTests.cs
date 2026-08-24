using System;
using System.IO;
using TelemetryDashboard.UI.Docking;
using TelemetryDashboard.UI.Services;

namespace TelemetryDashboard.Tests.Desktop;

/// <summary>
/// Where the choices an operator made are kept, and why it has to be outside the install.
/// </summary>
/// <remarks>
/// ToDo item 3 asks that presets be kept by the program and still be there after an update. Today
/// nothing in this product performs an update — the headless host reports that one exists and
/// applies nothing, and <c>GitHubUpdater.LaunchExternalPatcher</c> starts a script the operator
/// supplies rather than one this repository owns — so the requirement is a constraint on a feature
/// that does not exist yet.
/// <para>
/// That is exactly when a requirement gets lost. Whoever builds the updater will replace an install
/// directory, and whether the operator's settings survive is decided by where they were written,
/// months earlier, by somebody who was not thinking about updates. So the requirement is written
/// down as a check now, while both halves are still true, rather than as a line in a text file that
/// the update will quietly break.
/// </para>
/// <para>
/// What this does <em>not</em> cover is recorded rather than left to be discovered: the headless
/// host keeps its node identity, its <c>plugins/</c> and its <c>extensions/</c> beside the
/// executable, and none of those would survive replacing that directory. The node identity is the
/// sharpest of the three — ARCHITECTURE.md §2 argues that an identifier which quietly changes is
/// worse than none, and after an in-place update the same rig would publish under a new one, with
/// every coverage entry for the old id going silent and no error anywhere. Fixing it is not a move
/// of the file: identity is deliberately per-install, so that two hosts run from two directories on
/// one machine are two nodes, and any relocation has to keep that true. That is a decision for
/// whoever builds the updater, and this comment is the brief.
/// </para>
/// </remarks>
public class OperatorStateSurvivesAnUpdateTests
{
    private static string InstallDirectory => AppContext.BaseDirectory;

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheSettingsAnOperatorChoseAreNotKeptInsideTheInstall()
    {
        string settings = UiSettings.DefaultPath;

        settings.Should().StartWith(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "an update replaces the install directory, and preferences kept inside it go with it");

        IsInside(settings, InstallDirectory).Should().BeFalse(
            "anything beside the executable is what an update overwrites");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ThePanelArrangementIsKeptWhereAnUpdateCannotReachIt()
    {
        // The arrangement is a preset in every sense that matters to an operator: it is work they
        // did, it is not derivable from anything else, and losing it is invisible until the next
        // launch shows them somebody else's layout.
        string workspace = WorkspaceStore.DefaultPath;

        workspace.Should().StartWith(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        IsInside(workspace, InstallDirectory).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void BothLiveUnderOneDirectorySoAnUninstallCanFindThem()
    {
        // Kept together on purpose. State scattered across several roots is state nobody can back
        // up, move to a new machine, or remove when the product is uninstalled -- and the last of
        // those is how a "clean" reinstall silently inherits a broken setting.
        string settingsDirectory = Path.GetDirectoryName(UiSettings.DefaultPath)!;
        string workspaceDirectory = Path.GetDirectoryName(WorkspaceStore.DefaultPath)!;

        workspaceDirectory.Should().Be(settingsDirectory);
        Path.GetFileName(settingsDirectory).Should().Be("TelemetryDashboard");
    }

    private static bool IsInside(string path, string directory)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetFullPath(directory);

        if (!root.EndsWith(Path.DirectorySeparatorChar)) root += Path.DirectorySeparatorChar;

        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
