using TelemetryDashboard.UI.Services;

namespace TelemetryDashboard.Tests.Desktop.Tiers.Tier2_BoundaryCornerCases;

/// <summary>F13: VS Code-style command palette overlay boundary cases.</summary>
/// <remarks>
/// The palette owns visibility state driven by a WPF overlay and dispatches command actions on the
/// UI thread, so <c>CommandPaletteService</c> is a presentation-layer type and its tests belong on
/// the Windows side of the split.
/// </remarks>
public class F13_CommandPaletteBoundaryTests
{
    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F13_Boundary_EmptySearchQuery_ReturnsAllAvailableCommands()
    {
        var palette = new CommandPaletteService();
        palette.RegisterCommand("Cmd1", "Scope", () => { });
        palette.RegisterCommand("Cmd2", "Twin", () => { });

        var results = palette.FilterCommands("");
        results.Should().HaveCount(2);
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F13_Boundary_SpecialCharacterSearchQuery_RegexCharsEscaped()
    {
        var palette = new CommandPaletteService();
        palette.RegisterCommand("Zoom [100%]", "Scope", () => { });

        Action act = () => palette.FilterCommands(".*+?^${}()|[]\\");
        act.Should().NotThrow();
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F13_Boundary_NoMatchingCommandFound_DisplaysEmptyState()
    {
        var palette = new CommandPaletteService();
        palette.RegisterCommand("Cmd1", "Scope", () => { });

        var results = palette.FilterCommands("NON_EXISTENT_SEARCH_STRING");
        results.Should().BeEmpty();
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F13_Boundary_ExecuteNullCommandAction_DoesNotCrash()
    {
        var palette = new CommandPaletteService();
        palette.RegisterCommand("NullCmd", "Test", null);

        Action act = () => palette.ExecuteCommand("NullCmd");
        act.Should().NotThrow();
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F13_Boundary_RapidShortcutToggle_CtrlShiftP_MaintainsFocusState()
    {
        var palette = new CommandPaletteService();
        for (int i = 0; i < 20; i++)
        {
            palette.ToggleVisibility();
        }
        palette.IsVisible.Should().BeFalse();
    }
}
