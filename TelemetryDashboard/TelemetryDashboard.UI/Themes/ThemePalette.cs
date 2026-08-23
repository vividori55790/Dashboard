using System.Collections.Generic;
using System.Windows.Media;

namespace TelemetryDashboard.UI.Themes;

/// <summary>
/// What colour each token is, once per theme.
/// </summary>
/// <remarks>
/// Tokens.xaml declares the colours and the brushes that hold them; this declares what each colour
/// becomes in each theme. They are keyed by the same names, so a colour that exists in the
/// dictionary and not here is a colour that would not follow a theme change — and an architecture
/// test says so rather than leaving it to be noticed as one control that stayed dark.
/// <para>
/// Keyed by <em>colour</em>, not by brush, and that is the change that made the feature work. The
/// first version named the 33 brushes and set their <c>Color</c> properties, which is fine until
/// the brushes are frozen — as everything loaded from compiled BAML is. Colours are structs in a
/// dictionary: replacing one has no object identity to lose, and every brush bound to it with
/// <c>DynamicResource</c> repaints itself. One name per colour, and the brush list follows.
/// </para>
/// </remarks>
public static partial class ThemePalette
{
    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>The palette the application ships in, unchanged from the original tokens.</summary>
    public static IReadOnlyDictionary<string, Color> Dark { get; } = new Dictionary<string, Color>
    {
        ["CanvasColor"] = C("#0C0E12"),
        ["SurfaceColor"] = C("#14171C"),
        ["SurfaceAltColor"] = C("#1A1E25"),
        ["InsetColor"] = C("#090B0E"),

        ["BorderSubtleColor"] = C("#1F2329"),
        ["BorderDefaultColor"] = C("#2A2F37"),
        ["BorderStrongColor"] = C("#3A414B"),

        ["TextPrimaryColor"] = C("#E8EAED"),
        ["TextSecondaryColor"] = C("#9AA1AB"),
        ["TextTertiaryColor"] = C("#6C737D"),
        ["TextDisabledColor"] = C("#767D87"),

        ["AccentColor"] = C("#3D8BFF"),
        ["AccentHoverColor"] = C("#5C9EFF"),
        ["AccentPressedColor"] = C("#2E77E6"),
        ["AccentSubtleColor"] = C("#16233A"),
        ["OnAccentColor"] = C("#FFFFFF"),

        ["SuccessColor"] = C("#3FB950"),
        ["WarningColor"] = C("#D9A21B"),
        ["DangerColor"] = C("#F0524B"),
        ["InfoColor"] = C("#58A6FF"),

        ["SuccessSubtleColor"] = C("#132A17"),
        ["WarningSubtleColor"] = C("#2E2408"),
        ["DangerSubtleColor"] = C("#33130F"),
        ["DangerBorderColor"] = C("#5C2622"),
        ["ButtonHoverColor"] = C("#20242B"),

        ["Series1Color"] = C("#4C9AFF"),
        ["Series2Color"] = C("#F5A524"),
        ["Series3Color"] = C("#4CD4A0"),
        ["Series4Color"] = C("#C58AF9"),
        ["Series5Color"] = C("#FF7B72"),
        ["GridLineColor"] = C("#1C2027"),

        ["ScrimColor"] = C("#0C0E12"),
        ["ScrimStrongColor"] = C("#090B0E")
    };

    /// <summary>The palette for <paramref name="theme"/>, resolving System against the OS.</summary>
    public static IReadOnlyDictionary<string, Color> For(bool light) => light ? Light : Dark;
}
