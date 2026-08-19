using TelemetryDashboard.UI.Docking;

namespace TelemetryDashboard.Tests.Desktop.Tiers.Tier2_BoundaryCornerCases;

/// <summary>F11: AvalonDock workspace layout boundary cases.</summary>
/// <remarks>
/// <c>LayoutManager</c> attaches to an AvalonDock <c>DockingManager</c>, a WPF control, so the type
/// cannot be referenced from a portable assembly at all. Kept separate from the F12 workspace
/// cases below it so each file stays inside the 150-line micro-module limit.
/// </remarks>
public class F11_LayoutManagerBoundaryTests
{
    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F11_Boundary_CorruptedLayoutXml_RestoresDefaultPreset()
    {
        var layoutManager = new LayoutManager();
        string corruptedXml = "<AvalonDockLayout><MalformedXml";
        bool loaded = layoutManager.LoadLayoutFromXml(corruptedXml);

        loaded.Should().BeFalse();
        layoutManager.CurrentPreset.Should().Be(LayoutPreset.ScopeMode);
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F11_Boundary_FloatingWindowClosedUnexpectedly_MaintainsDockState()
    {
        var layoutManager = new LayoutManager();
        layoutManager.RegisterDockableWindow("ScopeView", isFloating: true);
        layoutManager.CloseWindow("ScopeView");

        layoutManager.IsWindowActive("ScopeView").Should().BeFalse();
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F11_Boundary_InvalidPresetIndex_FallsBackToScopePreset()
    {
        var layoutManager = new LayoutManager();
        layoutManager.ApplyPreset((LayoutPreset)999);

        layoutManager.CurrentPreset.Should().Be(LayoutPreset.ScopeMode);
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F11_Boundary_NullDockingManagerReference_ThrowsOrReturnsFalse()
    {
        var layoutManager = new LayoutManager();
        Action act = () => layoutManager.AttachDockingManager(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F11_Boundary_DuplicateDocumentId_HandlesWithoutColliding()
    {
        var layoutManager = new LayoutManager();
        layoutManager.RegisterDockableWindow("Doc1", isFloating: false);
        layoutManager.RegisterDockableWindow("Doc1", isFloating: false);

        layoutManager.GetRegisteredWindows().Count(w => w == "Doc1").Should().Be(1);
    }
}
