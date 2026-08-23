using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Rules that keep a theme switch reaching the screen.
/// </summary>
/// <remarks>
/// Each of these is a way the feature has already failed, and none of them is visible in a build,
/// a review or a green suite. A theme that stops working does not throw: it leaves one panel, or
/// one border, or one whole styled control on the colour it was built with, and the person who
/// notices is an operator six weeks later.
/// </remarks>
public partial class ArchitectureRuleTests
{
    /// <summary>
    /// A themed brush is referenced, never assigned.
    /// </summary>
    /// <remarks>
    /// The palette replaces its brushes when the theme changes, so any code holding the object
    /// rather than the key keeps the colour it was handed. Measured on the running window before
    /// this rule existed: three text blocks and a sparkline stayed on the previous palette, each
    /// from a line reading <c>Foreground = (Brush)FindResource(...)</c>. <c>SetResourceReference</c>
    /// is the same lookup and follows.
    /// <para>
    /// Reading a brush is still allowed — the 3D viewport has to, because a Helix material will not
    /// take a live brush and mints its own frozen copy from the colour. What is forbidden is
    /// putting the result into a property and walking away.
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Category", "Architecture")]
    public void NoCodeAssignsAThemeBrushInsteadOfReferencingIt()
    {
        var offenders = new List<string>();
        var assignment = new Regex(
            @"\.(Foreground|Background|BorderBrush|Fill|Stroke)\s*=\s*[^;]*(FindResource|TryFindResource)\s*\(",
            RegexOptions.Compiled);

        foreach (string file in ProductionSourceFiles())
        {
            if (!file.Contains("TelemetryDashboard.UI")) continue;

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (assignment.IsMatch(lines[i]))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}  {lines[i].Trim()}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "a brush read out of the dictionary is a snapshot; use SetResourceReference so the "
            + "control follows the palette instead of keeping the colour it was given");
    }

    /// <summary>
    /// Markup names a themed brush or colour dynamically, so replacing it is seen.
    /// </summary>
    /// <remarks>
    /// <c>StaticResource</c> resolves once, at load, and keeps the object. That is fine while the
    /// object is the one the palette repaints — and this application spent a release believing it
    /// was. It is not: WPF replaces a themed brush wholesale on a switch, and a style or template
    /// that captured the old one paints with it forever.
    /// <para>
    /// Tokens.xaml is exempt and has to be: its brushes resolve their colour at parse time on
    /// purpose, which is what allows them to be frozen, which is what stops WPF copying them per
    /// control.
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Category", "Architecture")]
    public void MarkupOutsideTheTokenFileNamesThemeResourcesDynamically()
    {
        var offenders = new List<string>();

        foreach (string file in XamlFiles())
        {
            if (Path.GetFileName(file) == "Tokens.xaml") continue;

            string markup = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match use in Regex.Matches(markup, @"\{StaticResource\s+([A-Za-z0-9]+(?:Brush|Color))\}"))
            {
                offenders.Add($"{Path.GetFileName(file)}: {use.Groups[1].Value}");
            }
        }

        offenders.Should().BeEmpty(
            "a StaticResource brush keeps the object it resolved and stops following the theme");
    }

    /// <summary>
    /// Both palettes name exactly the colours the token file declares.
    /// </summary>
    /// <remarks>
    /// A colour in the tokens and not in a palette is a control that does not change with the
    /// theme; a colour in a palette and not in the tokens is a line of the palette that does
    /// nothing. Neither is visible until somebody looks at the screen in the other theme.
    /// </remarks>
    [Fact]
    [Trait("Category", "Architecture")]
    public void ThePalettesAndTheTokenFileNameTheSameColours()
    {
        string tokens = File.ReadAllText(Path.Combine(
            SolutionRoot, "TelemetryDashboard.UI", "Themes", "Tokens.xaml"));

        var declared = new SortedSet<string>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match colour in Regex.Matches(tokens, @"<Color x:Key=""([A-Za-z0-9]+)"""))
        {
            declared.Add(colour.Groups[1].Value);
        }

        declared.Should().NotBeEmpty("the token file must declare colours");

        foreach (string palette in new[] { "ThemePalette.cs", "ThemePalette.Light.cs" })
        {
            string source = File.ReadAllText(Path.Combine(
                SolutionRoot, "TelemetryDashboard.UI", "Themes", palette));

            var named = new SortedSet<string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match entry in Regex.Matches(source, @"\[""([A-Za-z0-9]+Color)""\]"))
            {
                named.Add(entry.Groups[1].Value);
            }

            named.Should().Equal(declared, $"{palette} must cover every colour Tokens.xaml declares");
        }
    }
}
