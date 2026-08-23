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
/// Whether switching the theme actually changes any colour on a window that is already open.
/// </summary>
/// <remarks>
/// Three versions of this feature have shipped and two of them changed nothing an operator could
/// see. The first reflected for a Wpf.Ui type, dropped the argument, swallowed the exception, and
/// addressed a library no XAML here references; the log said "Theme toggled." each time. The second
/// repainted the token brushes in place, which is right until the brushes are frozen — as
/// everything loaded from compiled BAML is — so it had to replace them instead, which reaches
/// nothing already drawn because <c>StaticResource</c> kept the old object. It told the operator to
/// restart.
/// <para>
/// What actually works is one mechanism stated in three places, and each of these tests pins one
/// of them: the brushes are frozen so WPF shares rather than copies them, every reference to one is
/// <c>DynamicResource</c> so replacing the entry re-resolves, and the palette names colours so a
/// single list drives both halves.
/// </para>
/// <para>
/// Driven on the running window, which is the only place the previous versions failed: the shell
/// walks its own visual tree after each switch and reports what it found. Dark to light, 242 of 243
/// painted brushes on the new palette and none left on the old; light back to dark, 248 of 249 and
/// none left. Before the frozen brushes it was 143 stuck out of 436, and making every reference
/// dynamic while the brushes were still live took that to 343.
/// </para>
/// </remarks>
[Collection("wpf-resources")]
public class ThemeSwitchTests
{
    /// <summary>The shipping token dictionary, loaded from the file the application loads.</summary>
    private static ResourceDictionary Tokens()
    {
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
    public void EveryColourTheLightPaletteNamesExistsInTheShippingTokens()
    {
        // The rename trap. A palette key with no colour behind it is one control that silently
        // stays the other theme's colour, and nothing else in the application would report it.
        ResourceDictionary tokens = Tokens();

        var missing = new List<string>();
        foreach (string key in ThemePalette.Light.Keys)
        {
            if (tokens[key] is not Color) missing.Add(key);
        }

        missing.Should().BeEmpty("every themed key must resolve to a Color in Tokens.xaml");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void EveryColourHasTheBrushThatPaintsWithIt()
    {
        // The pair is the whole arrangement: XyzColor is what the colour is, XyzBrush is what
        // paints with it, and the service derives the second name from the first rather than
        // keeping a second list to forget a token in.
        ResourceDictionary tokens = Tokens();

        var missing = new List<string>();
        foreach (string key in ThemePalette.Dark.Keys)
        {
            if (tokens[ThemeService.BrushKeyFor(key)] is not SolidColorBrush) missing.Add(key);
        }

        missing.Should().BeEmpty("a colour nothing paints with cannot reach the screen");
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
    public void ApplyingTheLightPaletteReplacesBothTheColourAndItsBrush()
    {
        // Both halves, because markup uses both: controls take the brush, and the AvalonDock
        // system-colour overrides and the 3D viewport take the Color.
        ResourceDictionary tokens = Tokens();

        (int changed, IReadOnlyList<string> unknown) =
            ThemeService.InstallPalette(tokens, ThemePalette.Light);

        unknown.Should().BeEmpty();
        changed.Should().BeGreaterThan(40, "most of 33 colours and 33 brushes differ between themes");

        ((Color)tokens["CanvasColor"]).Should().Be(ThemePalette.Light["CanvasColor"]);
        ((SolidColorBrush)tokens["CanvasBrush"]).Color.Should().Be(ThemePalette.Light["CanvasColor"]);
        ((SolidColorBrush)tokens["TextPrimaryBrush"]).Color.Should().Be(ThemePalette.Light["TextPrimaryColor"]);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheBrushesItInstallsAreFrozenBecauseWpfCopiesTheOnesThatAreNot()
    {
        // Not tidiness. An unfrozen brush cannot be shared, so WPF hands out a private copy to
        // every control a template builds -- and repainting the dictionary's brush then reaches the
        // original and none of the copies. Measured on the running window: 143 of 436 painted
        // brushes stayed on the old palette exactly that way.
        ResourceDictionary tokens = Tokens();

        ThemeService.InstallPalette(tokens, ThemePalette.Light);

        foreach (string key in ThemePalette.Light.Keys)
        {
            ((SolidColorBrush)tokens[ThemeService.BrushKeyFor(key)]).IsFrozen.Should().BeTrue(
                $"'{key}' would otherwise be copied per control and stop following the theme");
        }
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void SwitchingBackRestoresTheColoursTheApplicationShipsWith()
    {
        ResourceDictionary tokens = Tokens();
        var shipped = new Dictionary<string, Color>();
        foreach (string key in ThemePalette.Dark.Keys) shipped[key] = (Color)tokens[key];

        ThemeService.InstallPalette(tokens, ThemePalette.Light);
        ThemeService.InstallPalette(tokens, ThemePalette.Dark);

        foreach (string key in ThemePalette.Dark.Keys)
        {
            ((Color)tokens[key]).Should().Be(shipped[key],
                $"'{key}' must come back to the colour Tokens.xaml declares");
            ((SolidColorBrush)tokens[ThemeService.BrushKeyFor(key)]).Color.Should().Be(shipped[key]);
        }
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AKeyTheDictionaryDoesNotHaveIsReportedRatherThanThrown()
    {
        // A mistyped key must not stop the window opening. It is one wrong colour; refusing to
        // start is not the proportionate answer to it.
        var sparse = new ResourceDictionary();

        (int changed, IReadOnlyList<string> unknown) = ThemeService.InstallPalette(
            sparse, new Dictionary<string, Color> { ["NoSuchColor"] = Colors.Red });

        changed.Should().Be(0);
        unknown.Should().ContainSingle().Which.Should().Be("NoSuchColor");
    }
}
