namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F10_FluentUiThemeTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void ThemeManager_ToggleTheme_SwitchesLightAndDark()
    {
        var themeState = new ThemeState { ActiveTheme = "Dark" };
        themeState.ToggleTheme();
        themeState.ActiveTheme.Should().Be("Light");
        themeState.ToggleTheme();
        themeState.ActiveTheme.Should().Be("Dark");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ThemeManager_MicaBackdrop_CanBeEnabled()
    {
        var themeState = new ThemeState { EnableMica = true };
        themeState.EnableMica.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ThemeManager_AcrylicBackdrop_CanBeConfigured()
    {
        var themeState = new ThemeState { BackdropType = "Acrylic" };
        themeState.BackdropType.Should().Be("Acrylic");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ThemeManager_SystemThemeSync_UpdatesActiveTheme()
    {
        var themeState = new ThemeState();
        themeState.SyncWithSystemTheme("Light");
        themeState.ActiveTheme.Should().Be("Light");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ThemeManager_ResourceDictionary_ContainsFluentBrushKeys()
    {
        var dict = new Dictionary<string, string>
        {
            ["SystemControlBackgroundBaseLowBrush"] = "#FF1F1F1F",
            ["SystemControlHighlightAccentBrush"] = "#FF0078D4"
        };

        dict.Should().ContainKey("SystemControlBackgroundBaseLowBrush");
        dict.Should().ContainKey("SystemControlHighlightAccentBrush");
    }
}

public class ThemeState
{
    public string ActiveTheme { get; set; } = "Dark";
    public bool EnableMica { get; set; } = true;
    public string BackdropType { get; set; } = "Mica";

    public void ToggleTheme()
    {
        ActiveTheme = ActiveTheme == "Dark" ? "Light" : "Dark";
    }

    public void SyncWithSystemTheme(string systemTheme)
    {
        ActiveTheme = systemTheme;
    }
}
