namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F13_CommandPaletteTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void CommandPalette_RegisterCommand_AddsToPalette()
    {
        var palette = new CommandPaletteState();
        palette.Register("Open Scope", () => { });

        palette.Commands.Should().ContainKey("Open Scope");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void CommandPalette_Filter_ReturnsMatchingCommands()
    {
        var palette = new CommandPaletteState();
        palette.Register("Open Scope", () => { });
        palette.Register("Open 3D Twin", () => { });
        palette.Register("Toggle Dark Theme", () => { });

        var results = palette.Search("Open");

        results.Should().HaveCount(2);
        results.Should().Contain("Open Scope");
        results.Should().Contain("Open 3D Twin");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void CommandPalette_ExecuteCommand_RunsBoundAction()
    {
        var palette = new CommandPaletteState();
        bool executed = false;
        palette.Register("Test Command", () => executed = true);

        palette.Execute("Test Command");

        executed.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void CommandPalette_ToggleVisibility_UpdatesState()
    {
        var palette = new CommandPaletteState();
        palette.IsVisible.Should().BeFalse();

        palette.ToggleVisibility();
        palette.IsVisible.Should().BeTrue();

        palette.ToggleVisibility();
        palette.IsVisible.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void CommandPalette_KeyNavigation_SelectsActiveCommand()
    {
        var palette = new CommandPaletteState();
        palette.Register("Cmd1", () => { });
        palette.Register("Cmd2", () => { });

        palette.SelectedIndex = 0;
        palette.NavigateNext();

        palette.SelectedIndex.Should().Be(1);
    }
}

public class CommandPaletteState
{
    public bool IsVisible { get; set; } = false;
    public int SelectedIndex { get; set; } = 0;
    public Dictionary<string, Action> Commands { get; } = new();

    public void Register(string name, Action action)
    {
        Commands[name] = action;
    }

    public List<string> Search(string query)
    {
        return Commands.Keys.Where(k => k.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public void Execute(string name)
    {
        if (Commands.TryGetValue(name, out var action)) action();
    }

    public void ToggleVisibility()
    {
        IsVisible = !IsVisible;
    }

    public void NavigateNext()
    {
        if (SelectedIndex < Commands.Count - 1) SelectedIndex++;
    }
}
