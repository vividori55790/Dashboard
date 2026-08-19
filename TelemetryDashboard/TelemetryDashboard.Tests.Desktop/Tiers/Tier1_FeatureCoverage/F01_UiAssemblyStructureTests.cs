namespace TelemetryDashboard.Tests.Desktop.Tiers.Tier1_FeatureCoverage;

/// <summary>
/// The presentation-layer half of the F01 solution-structure checks.
/// </summary>
/// <remarks>
/// The other four F01 tests assert that Core, Infrastructure and Plugins load, and none of them
/// needs a WPF assembly to do it — so they stayed in the portable project, where a Linux CI agent
/// actually exercises them. Only this one forces the WPF shell to load, which is precisely the
/// dependency that used to drag the entire suite onto Windows.
/// </remarks>
public class F01_UiAssemblyStructureTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void UIAssembly_LoadsSuccessfully_HasWpfTarget()
    {
        var type = typeof(TelemetryDashboard.UI.MainWindow);
        type.Assembly.FullName.Should().Contain("TelemetryDashboard.UI");
    }
}
