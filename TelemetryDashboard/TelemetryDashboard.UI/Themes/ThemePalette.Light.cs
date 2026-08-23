using System.Collections.Generic;
using System.Windows.Media;

namespace TelemetryDashboard.UI.Themes;

public static partial class ThemePalette
{
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
        ["CanvasColor"] = C("#F4F5F7"),
        ["SurfaceColor"] = C("#FFFFFF"),
        ["SurfaceAltColor"] = C("#F0F1F4"),
        ["InsetColor"] = C("#E8EAEE"),

        ["BorderSubtleColor"] = C("#E2E4E9"),
        ["BorderDefaultColor"] = C("#CDD1D9"),
        ["BorderStrongColor"] = C("#A8AEB9"),

        ["TextPrimaryColor"] = C("#16181D"),
        ["TextSecondaryColor"] = C("#4A515C"),
        ["TextTertiaryColor"] = C("#6B727D"),
        // Same reasoning as the dark palette's note: disabled must stay legible, because an
        // operator has to read a command to understand why it is unavailable.
        ["TextDisabledColor"] = C("#858C97"),

        ["AccentColor"] = C("#1667D9"),
        ["AccentHoverColor"] = C("#1257BC"),
        ["AccentPressedColor"] = C("#0E4899"),
        ["AccentSubtleColor"] = C("#E4EDFB"),
        ["OnAccentColor"] = C("#FFFFFF"),

        ["SuccessColor"] = C("#1A7F37"),
        ["WarningColor"] = C("#8A6100"),
        ["DangerColor"] = C("#C0342D"),
        ["InfoColor"] = C("#1667D9"),

        ["SuccessSubtleColor"] = C("#E6F4EA"),
        ["WarningSubtleColor"] = C("#FCF3DC"),
        ["DangerSubtleColor"] = C("#FBE9E8"),
        ["DangerBorderColor"] = C("#EBBAB7"),
        ["ButtonHoverColor"] = C("#E9EBEF"),

        // Darkened so a 1px trace stays visible on white. The dark palette's series colours are
        // tuned against #0C0E12 and several of them wash out entirely on a light ground.
        ["Series1Color"] = C("#1667D9"),
        ["Series2Color"] = C("#B36D00"),
        ["Series3Color"] = C("#0F8A63"),
        ["Series4Color"] = C("#7A4BC4"),
        ["Series5Color"] = C("#C0342D"),
        ["GridLineColor"] = C("#DFE2E7"),

        // The scrims stay dark in both themes. A modal overlay dims what is behind it, and a light
        // scrim over a light window dims nothing. This is why they are colours of their own rather
        // than the canvas colour they used to borrow.
        ["ScrimColor"] = C("#0C0E12"),
        ["ScrimStrongColor"] = C("#090B0E")
    };
}
