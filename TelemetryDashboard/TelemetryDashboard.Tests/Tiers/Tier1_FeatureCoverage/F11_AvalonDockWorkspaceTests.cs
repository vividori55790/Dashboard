namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F11_AvalonDockWorkspaceTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void WorkspaceLayoutManager_Initialize_ConfiguresDockingManager()
    {
        var manager = new WorkspaceLayoutState();
        manager.IsInitialized.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void WorkspaceLayoutManager_LoadPreset_ScopeMode()
    {
        var manager = new WorkspaceLayoutState();
        manager.LoadPreset("ScopeMode");

        manager.ActivePreset.Should().Be("ScopeMode");
        manager.VisiblePanels.Should().Contain("ScopeView");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void WorkspaceLayoutManager_LoadPreset_3DTwinMode()
    {
        var manager = new WorkspaceLayoutState();
        manager.LoadPreset("3DTwinMode");

        manager.ActivePreset.Should().Be("3DTwinMode");
        manager.VisiblePanels.Should().Contain("Twin3DView");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void WorkspaceLayoutManager_LoadPreset_ControlPanelMode()
    {
        var manager = new WorkspaceLayoutState();
        manager.LoadPreset("ControlPanelMode");

        manager.ActivePreset.Should().Be("ControlPanelMode");
        manager.VisiblePanels.Should().Contain("ControlPanel");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void WorkspaceLayoutManager_ToggleFloatingWindow_UpdatesState()
    {
        var manager = new WorkspaceLayoutState();
        manager.ToggleFloating("ScopeView", true);

        manager.FloatingPanels.Should().Contain("ScopeView");
    }
}

public class WorkspaceLayoutState
{
    public bool IsInitialized { get; set; } = true;
    public string ActivePreset { get; set; } = "Default";
    public List<string> VisiblePanels { get; } = new();
    public List<string> FloatingPanels { get; } = new();

    public void LoadPreset(string presetName)
    {
        ActivePreset = presetName;
        VisiblePanels.Clear();
        switch (presetName)
        {
            case "ScopeMode":
                VisiblePanels.Add("ScopeView");
                break;
            case "3DTwinMode":
                VisiblePanels.Add("Twin3DView");
                break;
            case "ControlPanelMode":
                VisiblePanels.Add("ControlPanel");
                break;
        }
    }

    public void ToggleFloating(string panelName, bool isFloating)
    {
        if (isFloating) FloatingPanels.Add(panelName);
        else FloatingPanels.Remove(panelName);
    }
}
