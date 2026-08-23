using System;
using System.Windows.Media;
using FluentAssertions;
using TelemetryDashboard.UI.Themes;
using Xunit;

namespace TelemetryDashboard.Tests.Desktop;

/// <summary>
/// Whether either palette can actually be read.
/// </summary>
/// <remarks>
/// "It changed colour" is not the test. A light theme is easy to produce and easy to produce badly:
/// the status greens and reds that carry meaning on a near-black ground are the first things to
/// disappear on a near-white one, and a control surface where "healthy" is the hardest word on the
/// screen to read is worse than no light theme at all.
/// <para>
/// WCAG AA for body text is 4.5:1. The pairs below are the ones this application actually renders,
/// and each threshold is the one that pair has to meet rather than a single number applied to
/// everything.
/// </para>
/// </remarks>
public class ThemeContrastTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void TheLightPaletteKeepsTextReadableAgainstItsOwnSurfaces()
    {
        (string Text, string Background, double Minimum)[] pairs =
        {
            ("TextPrimaryColor", "CanvasColor", 4.5),
            ("TextPrimaryColor", "SurfaceColor", 4.5),
            ("TextSecondaryColor", "SurfaceColor", 4.5),
            ("TextTertiaryColor", "SurfaceColor", 3.0),
            ("TextDisabledColor", "SurfaceColor", 2.8),
            ("OnAccentColor", "AccentColor", 4.5),
            ("DangerColor", "SurfaceColor", 3.0),
            ("SuccessColor", "SurfaceColor", 3.0),
            ("WarningColor", "SurfaceColor", 3.0)
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
            ("TextPrimaryColor", "CanvasColor", 4.5),
            ("TextPrimaryColor", "SurfaceColor", 4.5),
            ("TextSecondaryColor", "SurfaceColor", 4.5),
            ("TextTertiaryColor", "SurfaceColor", 3.0),
            // The token file records that this was raised from #474D55 after it measured about
            // 2.2:1 and a row of disabled buttons became unreadable. This is that fix, pinned.
            ("TextDisabledColor", "SurfaceColor", 2.8),
            ("OnAccentColor", "AccentColor", 3.0)
        };

        foreach ((string text, string background, double minimum) in pairs)
        {
            double ratio = Contrast(ThemePalette.Dark[text], ThemePalette.Dark[background]);
            ratio.Should().BeGreaterThanOrEqualTo(minimum,
                $"{text} on {background} in the dark theme reads at {ratio:F2}:1");
        }
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AScrimStaysDarkInBothThemesBecauseThatIsWhatDimmingIs()
    {
        // It used to be declared as the canvas colour, which is correct on the dark theme and
        // nonsense on the light one: a near-white veil over a near-white window dims nothing, and
        // the modal behind it stops reading as modal.
        foreach (string key in new[] { "ScrimColor", "ScrimStrongColor" })
        {
            Luminance(ThemePalette.Light[key]).Should().BeLessThan(0.05, $"{key} has to darken");
            ThemePalette.Light[key].Should().Be(ThemePalette.Dark[key]);
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
