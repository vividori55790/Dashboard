using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using TelemetryDashboard.UI.Services;
using TelemetryDashboard.UI.Themes;
using Xunit;

namespace TelemetryDashboard.Tests.Desktop;

/// <summary>
/// Whether switching the theme actually changes any colour.
/// </summary>
/// <remarks>
/// It did not. <c>ThemeService.ApplyTheme</c> reflected for a Wpf.Ui type, called
/// <c>GetMethod("Apply")</c> and invoked it with <c>null</c> arguments — the chosen theme was never
/// passed to anything, the ambiguous overload threw into a bare <c>catch</c>, and no XAML in the
/// application references Wpf.Ui in the first place. The button logged "Theme toggled." each time
/// and every pixel stayed where it was. There were no tests at all on this class.
/// <para>
/// These load the real <c>Themes/Tokens.xaml</c> rather than a dictionary built here, because the
/// failure this most needs to catch is a brush being renamed in the token file while the palette
/// goes on naming the old key — and a self-built dictionary would agree with the palette forever.
/// </para>
/// </remarks>
[Collection("wpf-resources")]
public class ThemeSwitchTests
{
    /// <summary>The shipping token dictionary, loaded from the file the application loads.</summary>
    private static ResourceDictionary Tokens()
    {
        // The XAML is copied beside the test binary by the UI project reference; fall back to the
        // source tree so a run from a different working directory still finds it.
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "Themes", "Tokens.xaml"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                         "TelemetryDashboard.UI", "Themes", "Tokens.xaml")
        };

        foreach (string path in candidates)
        {
            if (!File.Exists(path)) continue;
            using FileStream stream = File.OpenRead(path);
            return (ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream);
        }

        throw new FileNotFoundException(
            "Tokens.xaml was not found beside the tests or in the source tree: "
            + string.Join(", ", candidates));
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void EveryBrushTheLightPaletteNamesExistsInTheShippingTokens()
    {
        // The rename trap. A palette key with no brush behind it is one control that silently stays
        // the other theme's colour, and nothing else in the application would report it.
        ResourceDictionary tokens = Tokens();

        var missing = new List<string>();
        foreach (string key in ThemePalette.Light.Keys)
        {
            if (tokens[key] is not SolidColorBrush) missing.Add(key);
        }

        missing.Should().BeEmpty("every themed key must resolve to a brush in Tokens.xaml");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheTwoPalettesCoverExactlyTheSameKeys()
    {
        ThemePalette.Light.Keys.Should().BeEquivalentTo(ThemePalette.Dark.Keys,
            "a key in one palette and not the other is a colour that changes in one direction only");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ApplyingTheLightPaletteChangesTheBrushesThemselves()
    {
        // The mechanism the whole feature rests on: consumers hold the brush object, not its
        // colour, so mutating Color in place reaches all 900 StaticResource references without a
        // single markup change. If a brush were ever frozen, this is where it would show up.
        ResourceDictionary tokens = Tokens();
        var before = new Dictionary<string, Color>();
        foreach (string key in ThemePalette.Light.Keys) before[key] = ((SolidColorBrush)tokens[key]).Color;

        (int repainted, int replaced, IReadOnlyList<string> unknown) =
            ThemeService.InstallPalette(tokens, ThemePalette.Light);

        unknown.Should().BeEmpty();
        (repainted + replaced).Should().Be(ThemePalette.Light.Count);
        repainted.Should().Be(ThemePalette.Light.Count,
            "XamlReader does not freeze, so this path repaints in place -- the running application "
            + "takes the replacement path instead, and that difference is exactly why a passing "
            + "test here did not mean a working theme there");

        ((SolidColorBrush)tokens["CanvasBrush"]).Color.Should().Be(ThemePalette.Light["CanvasBrush"]);
        ((SolidColorBrush)tokens["TextPrimaryBrush"]).Color.Should().Be(ThemePalette.Light["TextPrimaryBrush"]);

        int changed = 0;
        foreach (string key in ThemePalette.Light.Keys)
        {
            if (((SolidColorBrush)tokens[key]).Color != before[key]) changed++;
        }

        changed.Should().BeGreaterThan(20,
            "a light theme that leaves nearly every brush where it was is not a light theme");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void SwitchingBackRestoresTheColoursTheApplicationShipsWith()
    {
        ResourceDictionary tokens = Tokens();
        var shipped = new Dictionary<string, Color>();
        foreach (string key in ThemePalette.Dark.Keys) shipped[key] = ((SolidColorBrush)tokens[key]).Color;

        ThemeService.InstallPalette(tokens, ThemePalette.Light);
        ThemeService.InstallPalette(tokens, ThemePalette.Dark);

        foreach (string key in ThemePalette.Dark.Keys)
        {
            ((SolidColorBrush)tokens[key]).Color.Should().Be(shipped[key],
                $"'{key}' must come back to the colour Tokens.xaml declares");
        }
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheLightPaletteKeepsTextReadableAgainstItsOwnSurfaces()
    {
        // A theme nobody can read is not a working theme, and "it changed colour" is not the test.
        // WCAG AA for body text is 4.5:1; these are the pairs the application actually renders.
        (string Text, string Background, double Minimum)[] pairs =
        {
            ("TextPrimaryBrush", "CanvasBrush", 4.5),
            ("TextPrimaryBrush", "SurfaceBrush", 4.5),
            ("TextSecondaryBrush", "SurfaceBrush", 4.5),
            ("TextTertiaryBrush", "SurfaceBrush", 3.0),
            ("TextDisabledBrush", "SurfaceBrush", 2.8),
            ("OnAccentBrush", "AccentBrush", 4.5),
            ("DangerBrush", "SurfaceBrush", 3.0),
            ("SuccessBrush", "SurfaceBrush", 3.0),
            ("WarningBrush", "SurfaceBrush", 3.0)
        };

        foreach ((string text, string background, double minimum) in pairs)
        {
            double ratio = Contrast(ThemePalette.Light[text], ThemePalette.Light[background]);
            ratio.Should().BeGreaterThanOrEqualTo(minimum,
                $"{text} on {background} in the light theme reads at {ratio:F2}:1");
        }
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheDarkPaletteIsHeldToTheSameStandard()
    {
        (string Text, string Background, double Minimum)[] pairs =
        {
            ("TextPrimaryBrush", "CanvasBrush", 4.5),
            ("TextPrimaryBrush", "SurfaceBrush", 4.5),
            ("TextSecondaryBrush", "SurfaceBrush", 4.5),
            ("TextTertiaryBrush", "SurfaceBrush", 3.0),
            // The token file records that this was raised from #474D55 after it measured about
            // 2.2:1 and a row of disabled buttons became unreadable. This is that fix, pinned.
            ("TextDisabledBrush", "SurfaceBrush", 2.8),
            ("OnAccentBrush", "AccentBrush", 3.0)
        };

        foreach ((string text, string background, double minimum) in pairs)
        {
            double ratio = Contrast(ThemePalette.Dark[text], ThemePalette.Dark[background]);
            ratio.Should().BeGreaterThanOrEqualTo(minimum,
                $"{text} on {background} in the dark theme reads at {ratio:F2}:1");
        }
    }

    /// <summary>WCAG relative-contrast ratio between two opaque colours.</summary>
    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}
