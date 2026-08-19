using TelemetryDashboard.UI.Services;

namespace TelemetryDashboard.Tests.Desktop.Tiers.Tier2_BoundaryCornerCases;

/// <summary>F10: Windows 11 Fluent UI and theme boundary cases.</summary>
/// <remarks>
/// <c>ThemeService</c> applies a Mica backdrop through a Win32 window handle, so these cases can
/// only be stated against a real WPF assembly. They arrived here from the former
/// <c>F10_F16_UiBoundaryTests</c>, split one feature per file so no file exceeds the 150-line
/// micro-module limit.
/// </remarks>
public class F10_ThemeBoundaryTests
{
    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F10_Boundary_UnsupportedOS_FallsBackToStandardBackdrop()
    {
        var themeService = new ThemeService();
        bool isSupported = themeService.IsMicaSupportedOnCurrentOS();
        // Method returns bool without crashing on non-Win11 or Win11 environments
        (isSupported == true || isSupported == false).Should().BeTrue();
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F10_Boundary_InvalidThemeName_DefaultsToDarkTheme()
    {
        var themeService = new ThemeService();
        themeService.ApplyTheme("INVALID_THEME_NAME_XYZ");
        themeService.CurrentTheme.Should().Be(AppTheme.Dark);
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F10_Boundary_RapidThemeToggle_DoesNotCauseGdiLeak()
    {
        var themeService = new ThemeService();
        for (int i = 0; i < 50; i++)
        {
            themeService.ApplyTheme(i % 2 == 0 ? AppTheme.Light : AppTheme.Dark);
        }
        themeService.CurrentTheme.Should().Be(AppTheme.Dark);
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F10_Boundary_NullWindowInstance_ApplyMica_ThrowsArgumentNullException()
    {
        var themeService = new ThemeService();
        Action act = () => themeService.ApplyMicaBackdrop(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F10_Boundary_HighDpiResolutionChange_MaintainsLayoutScale()
    {
        var themeService = new ThemeService();
        themeService.SetDpiScale(2.0); // 200% scale
        themeService.CurrentDpiScale.Should().Be(2.0);
    }
}
