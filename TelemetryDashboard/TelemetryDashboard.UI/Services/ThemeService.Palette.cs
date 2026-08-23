using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using TelemetryDashboard.UI.Themes;

namespace TelemetryDashboard.UI.Services;

public sealed partial class ThemeService
{
    /// <summary>The brush key that holds a colour key, by the naming rule Tokens.xaml keeps.</summary>
    /// <remarks>
    /// Derived rather than listed. A second list would be a second place to forget a token, and
    /// the pair is the point: <c>CanvasColor</c> is what the colour is and <c>CanvasBrush</c> is
    /// what paints with it. An architecture test holds the file to the same rule.
    /// </remarks>
    public static string BrushKeyFor(string colourKey) =>
        colourKey.EndsWith("Color", StringComparison.Ordinal)
            ? string.Concat(colourKey.AsSpan(0, colourKey.Length - 5), "Brush")
            : colourKey + "Brush";

    /// <summary>
    /// Puts a palette into a resource dictionary: the colours, and the brushes that hold them.
    /// </summary>
    /// <remarks>
    /// Both, and the reason is a WPF rule that is invisible until it is measured. A brush is a
    /// <c>Freezable</c>, and an <em>unfrozen</em> one cannot be shared safely, so WPF quietly hands
    /// out per-instance copies of it — once for every control a template builds, and once for every
    /// <c>DynamicResource</c> that resolves it. Repainting the dictionary's brush then reaches the
    /// original and none of the copies. Measured on the running window: 143 of 436 painted brushes
    /// stayed on the old palette that way, and going the other way and making every reference
    /// dynamic took it to 343.
    /// <para>
    /// So the brushes in Tokens.xaml are frozen on purpose. A frozen brush is shared, never copied,
    /// and cannot be repainted — which is exactly right, because the way to change one is to put a
    /// different brush under its key and let the <c>DynamicResource</c> references re-resolve. The
    /// colours are replaced alongside for the markup that needs a <c>Color</c> rather than a brush:
    /// the AvalonDock system-colour overrides and the 3D viewport, which builds its own frozen
    /// materials.
    /// </para>
    /// </remarks>
    /// <returns>How many entries changed, and any colour key the dictionary does not have.</returns>
    public static (int Changed, IReadOnlyList<string> Unknown) InstallPalette(
        ResourceDictionary resources, IReadOnlyDictionary<string, Color> palette)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(palette);

        var missing = new List<string>();
        int changed = 0;

        foreach ((string colourKey, Color colour) in palette)
        {
            if (resources[colourKey] is not Color current) { missing.Add(colourKey); continue; }

            if (current != colour)
            {
                resources[colourKey] = colour;
                changed++;
            }

            string brushKey = BrushKeyFor(colourKey);
            if (resources[brushKey] is not SolidColorBrush brush) { missing.Add(brushKey); continue; }
            if (brush.Color == colour) continue;

            var replacement = new SolidColorBrush(colour);
            replacement.Freeze();
            resources[brushKey] = replacement;
            changed++;
        }

        return (changed, missing);
    }

    /// <summary>
    /// Installs the stored palette before the first window is built.
    /// </summary>
    /// <remarks>
    /// Still done at start-up even though a later switch now works, because the alternative is a
    /// window that paints dark and then flips to light in front of the operator.
    /// </remarks>
    /// <returns>How many entries were set.</returns>
    public static int InstallStoredPaletteAtStartup(ResourceDictionary resources, UiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        AppTheme stored = Enum.TryParse(settings.Theme, ignoreCase: true, out AppTheme theme)
            ? theme
            : AppTheme.Dark;

        (int changed, _) = InstallPalette(resources, ThemePalette.For(Resolve(stored)));
        return changed;
    }
}
