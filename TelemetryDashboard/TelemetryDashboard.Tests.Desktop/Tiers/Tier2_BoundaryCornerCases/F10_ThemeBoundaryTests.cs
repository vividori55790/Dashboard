using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using TelemetryDashboard.UI.Services;
using TelemetryDashboard.UI.Themes;

namespace TelemetryDashboard.Tests.Desktop.Tiers.Tier2_BoundaryCornerCases;

/// <summary>F10: theme boundary cases.</summary>
/// <remarks>
/// These used to assert against a Mica backdrop and a DPI scale. Both are gone. The backdrop was
/// reflection at <c>Wpf.Ui.Appearance.WindowBackdrop.ApplyBackdrop</c> invoked with the wrong
/// number of arguments into a bare <c>catch</c>, on a window whose background is opaque, so a Mica
/// surface would have had nothing to show through even had the call landed; the DPI scale was a
/// number stored in a property no code ever read. The test that asserted the backdrop threw on null
/// was the only thing keeping either alive.
/// <para>
/// What replaces them are the boundaries this feature really has: an unreadable stored value, a
/// switch repeated until something leaks, and the third theme state on a machine whose Windows
/// setting cannot be read.
/// </para>
/// </remarks>
public class F10_ThemeBoundaryTests : IDisposable
{
    private readonly string _settingsPath = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "td-theme-" + Guid.NewGuid().ToString("N") + ".json");

    // Never the real settings file. A service that can only be exercised by writing over the
    // preferences of whoever runs the tests is a service nobody runs the tests on twice.
    private UiSettings Settings(string theme) => new() { Theme = theme, Origin = _settingsPath };

    public void Dispose()
    {
        try { System.IO.File.Delete(_settingsPath); } catch (System.IO.IOException) { }
        GC.SuppressFinalize(this);
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F10_Boundary_InvalidThemeName_DefaultsToDarkTheme()
    {
        using var themeService = new ThemeService(Settings("Dark"));

        themeService.ApplyTheme("INVALID_THEME_NAME_XYZ");

        themeService.CurrentTheme.Should().Be(AppTheme.Dark);
        themeService.EffectiveIsLight.Should().BeFalse();
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F10_Boundary_AStoredValueNobodyCanReadOpensInTheShippedPalette()
    {
        // Settings files are edited by hand and survive upgrades. Refusing to start over one is
        // out of proportion; opening in the palette the application ships in is not.
        using var themeService = new ThemeService(Settings("Solarized"));

        themeService.CurrentTheme.Should().Be(AppTheme.Dark);
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F10_Boundary_RapidThemeToggle_LeavesTheChoiceWhereItEnded()
    {
        using var themeService = new ThemeService(Settings("Dark"));

        for (int i = 0; i < 50; i++)
        {
            themeService.ApplyTheme(i % 2 == 0 ? AppTheme.Light : AppTheme.Dark);
        }

        themeService.CurrentTheme.Should().Be(AppTheme.Dark);
        themeService.FollowsSystem.Should().BeFalse();
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F10_Boundary_FollowingTheSystemResolvesToOneOfTheTwoRealPalettes()
    {
        // System is a choice, not a palette. Whatever Windows says -- including a machine where the
        // setting cannot be read at all -- what gets painted has to be one of the two that exist.
        using var themeService = new ThemeService(Settings("System"));

        themeService.ApplyTheme(AppTheme.System);

        themeService.FollowsSystem.Should().BeTrue();
        themeService.EffectiveIsLight.Should().Be(SystemTheme.IsLight());
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F10_Boundary_ReadingTheWindowsSettingNeverThrows()
    {
        // It is a registry read on somebody else's locked-down machine. Being unable to answer is a
        // normal outcome and has to be reported as one.
        Action read = () => SystemTheme.TryReadIsLight(out _);

        read.Should().NotThrow();
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F10_Boundary_InstallingAPaletteTwiceChangesNothingTheSecondTime()
    {
        // The count is what the operator is shown, so it has to mean work done rather than work
        // attempted.
        var tokens = new ResourceDictionary
        {
            ["CanvasColor"] = Colors.Black,
            ["CanvasBrush"] = new SolidColorBrush(Colors.Black)
        };
        var palette = new Dictionary<string, Color> { ["CanvasColor"] = Colors.White };

        ThemeService.InstallPalette(tokens, palette).Changed.Should().Be(2);
        ThemeService.InstallPalette(tokens, palette).Changed.Should().Be(0);
    }
}
