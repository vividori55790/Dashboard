using System.Collections.Generic;
using System.Windows.Media;

namespace TelemetryDashboard.UI.Themes;

/// <summary>
/// The colour behind every brush key, once per theme.
/// </summary>
/// <remarks>
/// Tokens.xaml declares the brushes and this declares what colour each one is in each theme. They
/// are keyed by the same names, so a brush that exists in the dictionary and not here is a brush
/// that would not follow a theme change — and an architecture test says so rather than leaving it
/// to be noticed as one control that stayed dark.
/// <para>
/// Why this can be applied at all is worth writing down, because the obvious reading of the markup
/// says it cannot. Every consumer refers to these brushes with <c>StaticResource</c>, which
/// resolves once at load and never looks again — 900 references, none of which would notice a new
/// dictionary being merged in. But <c>StaticResource</c> resolves to an <em>object</em>, and all
/// 900 hold the same one. None of the brushes is frozen, so changing a brush's <c>Color</c> in
/// place is seen by every element painted with it. The theme switch is 33 property sets, and no
/// markup had to change.
/// </para>
/// </remarks>
public static class ThemePalette
{
    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>The palette the application ships in, unchanged from the original tokens.</summary>
    public static IReadOnlyDictionary<string, Color> Dark { get; } = new Dictionary<string, Color>
    {
        ["CanvasBrush"] = C("#0C0E12"),
        ["SurfaceBrush"] = C("#14171C"),
        ["SurfaceAltBrush"] = C("#1A1E25"),
        ["InsetBrush"] = C("#090B0E"),

        ["BorderSubtleBrush"] = C("#1F2329"),
        ["BorderDefaultBrush"] = C("#2A2F37"),
        ["BorderStrongBrush"] = C("#3A414B"),

        ["TextPrimaryBrush"] = C("#E8EAED"),
        ["TextSecondaryBrush"] = C("#9AA1AB"),
        ["TextTertiaryBrush"] = C("#6C737D"),
        ["TextDisabledBrush"] = C("#767D87"),

        ["AccentBrush"] = C("#3D8BFF"),
        ["AccentHoverBrush"] = C("#5C9EFF"),
        ["AccentPressedBrush"] = C("#2E77E6"),
        ["AccentSubtleBrush"] = C("#16233A"),
        ["OnAccentBrush"] = C("#FFFFFF"),

        ["SuccessBrush"] = C("#3FB950"),
        ["WarningBrush"] = C("#D9A21B"),
        ["DangerBrush"] = C("#F0524B"),
        ["InfoBrush"] = C("#58A6FF"),

        ["SuccessSubtleBrush"] = C("#132A17"),
        ["WarningSubtleBrush"] = C("#2E2408"),
        ["DangerSubtleBrush"] = C("#33130F"),
        ["DangerBorderBrush"] = C("#5C2622"),
        ["ButtonHoverBrush"] = C("#20242B"),

        ["Series1Brush"] = C("#4C9AFF"),
        ["Series2Brush"] = C("#F5A524"),
        ["Series3Brush"] = C("#4CD4A0"),
        ["Series4Brush"] = C("#C58AF9"),
        ["Series5Brush"] = C("#FF7B72"),
        ["GridLineBrush"] = C("#1C2027"),

        ["ScrimBrush"] = C("#0C0E12"),
        ["ScrimStrongBrush"] = C("#090B0E")
    };

    /// <summary>
    /// A light palette built to the same rules the dark one states.
    /// </summary>
    /// <remarks>
    /// Not an inversion. The surfaces step the other way — a light interface reads depth as
    /// <em>darker</em> borders on a near-white ground rather than as lighter fills — and the status
    /// colours are darkened rather than reused, because #3FB950 on white is about 2.3:1 and would
    /// make "healthy" the hardest word on the screen to read. The accent is darkened for the same
    /// reason: it is used as a fill behind white text and as text on a light ground, and one hue
    /// cannot do both at these contrasts.
    /// <para>
    /// The subtle status fills invert in role: on dark they are near-black tints, on light they are
    /// near-white tints, so a status band stays quieter than the text sitting on it either way.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, Color> Light { get; } = new Dictionary<string, Color>
    {
        ["CanvasBrush"] = C("#F4F5F7"),
        ["SurfaceBrush"] = C("#FFFFFF"),
        ["SurfaceAltBrush"] = C("#F0F1F4"),
        ["InsetBrush"] = C("#E8EAEE"),

        ["BorderSubtleBrush"] = C("#E2E4E9"),
        ["BorderDefaultBrush"] = C("#CDD1D9"),
        ["BorderStrongBrush"] = C("#A8AEB9"),

        ["TextPrimaryBrush"] = C("#16181D"),
        ["TextSecondaryBrush"] = C("#4A515C"),
        ["TextTertiaryBrush"] = C("#6B727D"),
        // Same reasoning as the dark palette's note: disabled must stay legible, because an
        // operator has to read a command to understand why it is unavailable.
        ["TextDisabledBrush"] = C("#858C97"),

        ["AccentBrush"] = C("#1667D9"),
        ["AccentHoverBrush"] = C("#1257BC"),
        ["AccentPressedBrush"] = C("#0E4899"),
        ["AccentSubtleBrush"] = C("#E4EDFB"),
        ["OnAccentBrush"] = C("#FFFFFF"),

        ["SuccessBrush"] = C("#1A7F37"),
        ["WarningBrush"] = C("#8A6100"),
        ["DangerBrush"] = C("#C0342D"),
        ["InfoBrush"] = C("#1667D9"),

        ["SuccessSubtleBrush"] = C("#E6F4EA"),
        ["WarningSubtleBrush"] = C("#FCF3DC"),
        ["DangerSubtleBrush"] = C("#FBE9E8"),
        ["DangerBorderBrush"] = C("#EBBAB7"),
        ["ButtonHoverBrush"] = C("#E9EBEF"),

        // Darkened so a 1px trace stays visible on white. The dark palette's series colours are
        // tuned against #0C0E12 and several of them wash out entirely on a light ground.
        ["Series1Brush"] = C("#1667D9"),
        ["Series2Brush"] = C("#B36D00"),
        ["Series3Brush"] = C("#0F8A63"),
        ["Series4Brush"] = C("#7A4BC4"),
        ["Series5Brush"] = C("#C0342D"),
        ["GridLineBrush"] = C("#DFE2E7"),

        // The scrims stay dark in both themes. A modal overlay dims what is behind it, and a light
        // scrim over a light window dims nothing.
        ["ScrimBrush"] = C("#0C0E12"),
        ["ScrimStrongBrush"] = C("#090B0E")
    };

    public static IReadOnlyDictionary<string, Color> For(bool light) => light ? Light : Dark;
}
