using System.Text.RegularExpressions;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

/// <summary>
/// F10: what the theme feature has to be, stated where the product cannot be referenced.
/// </summary>
/// <remarks>
/// This file used to declare a class called <c>ThemeState</c> at the bottom of itself and then test
/// that class. Toggling its own field flipped its own string; syncing with the system assigned the
/// argument it was handed; the resource-dictionary test built a dictionary, put two keys in it, and
/// asserted the two keys were in it. Five green tests, and not one of them touched
/// <c>ThemeService</c>, <c>ThemePalette</c> or a single line of shipping code — the whole time the
/// real theme button was calling a Wpf.Ui method with the argument dropped and logging
/// "Theme toggled."
/// <para>
/// This project cannot reference WPF, so the palette and the service are exercised in
/// TelemetryDashboard.Tests.Desktop, where a <c>ResourceDictionary</c> and a visual tree exist.
/// What belongs here is the part of the design that is not about WPF at all: the naming rule that
/// ties a colour to the brush that paints with it. It is stated in three places — the token file,
/// the palette, and the service that derives one key from the other — and a rule stated three times
/// is a rule that will be broken once.
/// </para>
/// </remarks>
public class F10_FluentUiThemeTests
{
    /// <summary>The rule <c>ThemeService.BrushKeyFor</c> implements, restated independently.</summary>
    private static string BrushKeyFor(string colourKey) =>
        colourKey.EndsWith("Color", StringComparison.Ordinal)
            ? colourKey[..^5] + "Brush"
            : colourKey + "Brush";

    [Theory]
    [Trait("Category", "Tier1")]
    [InlineData("CanvasColor", "CanvasBrush")]
    [InlineData("TextPrimaryColor", "TextPrimaryBrush")]
    [InlineData("Series1Color", "Series1Brush")]
    [InlineData("ScrimStrongColor", "ScrimStrongBrush")]
    public void EveryColourNamesTheBrushThatPaintsWithIt(string colour, string brush)
    {
        BrushKeyFor(colour).Should().Be(brush);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheTokenFileDeclaresBothHalvesOfEveryPair()
    {
        // Read off the shipping XAML rather than a list kept here, because the failure worth
        // catching is somebody adding a colour and forgetting the brush -- at which point the
        // theme silently stops reaching whatever that colour was for.
        string tokens = File.ReadAllText(TokenFilePath());

        var missing = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in Regex.Matches(tokens, @"<Color x:Key=""([A-Za-z0-9]+)"""))
        {
            string brush = BrushKeyFor(match.Groups[1].Value);
            if (!tokens.Contains($@"x:Key=""{brush}""", StringComparison.Ordinal)) missing.Add(brush);
        }

        missing.Should().BeEmpty("a colour nothing paints with cannot reach the screen");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void NoTokenBrushStatesAColourOfItsOwn()
    {
        // The one rule the whole mechanism rests on. A brush with a literal colour is a brush the
        // palette does not drive, and it fails the way that costs the most: invisibly, as the one
        // control that stayed dark.
        string tokens = File.ReadAllText(TokenFilePath());

        var literals = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in Regex.Matches(tokens, @"<SolidColorBrush[^>]*x:Key=""([A-Za-z0-9]+)""[^>]*>"))
        {
            if (Regex.IsMatch(match.Value, @"Color=""#")) literals.Add(match.Groups[1].Value);
        }

        literals.Should().BeEmpty("every token brush takes its colour from a Color token");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void EveryTokenBrushIsFrozenOnPurpose()
    {
        // Measured, not preferred. WPF cannot share an unfrozen brush, so it hands a private copy
        // to every control a template builds, and replacing the dictionary entry then reaches the
        // original and none of the copies: 143 of 436 painted brushes on the running window stayed
        // on the old palette that way.
        string tokens = File.ReadAllText(TokenFilePath());

        int brushes = Regex.Matches(tokens, "<SolidColorBrush").Count;
        int frozen = Regex.Matches(tokens, @"po:Freeze=""True""").Count;

        brushes.Should().BeGreaterThan(20);
        frozen.Should().Be(brushes, "an unfrozen token brush is copied per control and stops following");
    }

    private static string TokenFilePath()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "TelemetryDashboard.UI", "Themes", "Tokens.xaml");

        File.Exists(path).Should().BeTrue($"the token file must be readable at {path}");
        return path;
    }
}
