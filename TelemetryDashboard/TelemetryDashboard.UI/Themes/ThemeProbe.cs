using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TelemetryDashboard.UI.Themes;

/// <summary>What a walk of the live visual tree found painted on it.</summary>
/// <param name="Painted">Opaque solid-colour brushes actually rendered by realised elements.</param>
/// <param name="Active">How many carry a colour from the palette that is supposed to be showing.</param>
/// <param name="Stale">How many still carry a colour from the palette that is not.</param>
/// <param name="Custom">How many carry a colour neither palette declares.</param>
/// <param name="Stragglers">The stale ones, grouped and counted, so the report is actionable.</param>
public sealed record ThemeProbeResult(
    int Painted, int Active, int Stale, int Custom, IReadOnlyList<string> Stragglers)
{
    /// <summary>The one line worth putting in front of an operator.</summary>
    public string Describe()
    {
        string line = $"{Painted} painted brushes on screen: {Active} on the new palette, "
                    + $"{Stale} still on the old, {Custom} outside both.";

        if (Stale > 0) line += " Stuck: " + string.Join(", ", Stragglers) + ".";
        return line;
    }
}

/// <summary>
/// Reads back what the window is actually painted with.
/// </summary>
/// <remarks>
/// The reason this exists rather than a screenshot: screen capture on the machine this is developed
/// on returns a blank image, and every other check available — that the service reports success,
/// that the dictionary holds the new colour, that a test dictionary repaints — has already been
/// passed by a version of this feature that changed nothing an operator could see. Walking the
/// realised visual tree and asking each element what brush it is holding is the one measurement
/// that could not have passed while the window stayed dark.
/// <para>
/// Four element types, and the list is deliberate rather than lazy. <c>Border</c>, <c>TextBlock</c>,
/// <c>Panel</c> and <c>Shape</c> render these properties themselves, so a value read back is a
/// colour on the glass. A templated <c>Control</c> does not: its template paints a <c>Border</c>
/// inside itself and often never binds the control's own <c>BorderBrush</c>, which then sits at
/// whatever WPF defaulted it to. Reading those made this report claim 37 unthemed brushes on a
/// window that renders none of them — a number that would have sent somebody hunting a fault that
/// was not there.
/// </para>
/// <para>
/// It counts what is realised, which is the honest scope: WPF does not build the contents of an
/// unselected tab, so those elements are not in the tree to be asked, and will be built from the
/// current palette when the operator reaches them.
/// </para>
/// </remarks>
public static class ThemeProbe
{
    private const int MaxStragglers = 5;

    /// <summary>Walks <paramref name="root"/> and classifies every colour it is painted with.</summary>
    public static ThemeProbeResult Sample(
        DependencyObject? root,
        IReadOnlyDictionary<string, Color> active,
        IReadOnlyDictionary<string, Color> other)
    {
        var activeColours = new HashSet<Color>(active.Values);
        var otherColours = new HashSet<Color>(other.Values);
        var stuck = new Dictionary<string, int>();
        int painted = 0, onActive = 0, stale = 0, custom = 0;

        void Look(string what, Brush? brush, DependencyObject owner)
        {
            if (brush is not SolidColorBrush solid || solid.Color.A == 0) return;

            painted++;

            if (activeColours.Contains(solid.Color)) { onActive++; return; }

            if (!otherColours.Contains(solid.Color)) { custom++; return; }

            stale++;

            // Grouped rather than listed. Eighty-seven borders stuck the same way is one fact, and
            // five lines repeating the first of them is not a report anyone can act on. Whether the
            // brush is frozen says which of the two faults it is: a live per-instance copy nobody
            // repainted, or a snapshot somebody assigned instead of referencing.
            string signature =
                $"{owner.GetType().Name}.{what} {Hex(solid.Color)} ({(solid.IsFrozen ? "frozen" : "live")})";
            stuck[signature] = stuck.TryGetValue(signature, out int seen) ? seen + 1 : 1;
        }

        Walk(root, Look);

        var stragglers = new List<string>();
        foreach ((string signature, int count) in Worst(stuck))
        {
            if (stragglers.Count >= MaxStragglers) break;
            stragglers.Add(count > 1 ? $"{signature} x{count}" : signature);
        }

        return new ThemeProbeResult(painted, onActive, stale, custom, stragglers);
    }

    /// <summary>Biggest group first, so a capped list shows the worst rather than the first.</summary>
    private static IEnumerable<KeyValuePair<string, int>> Worst(Dictionary<string, int> stuck)
    {
        var ordered = new List<KeyValuePair<string, int>>(stuck);
        ordered.Sort((a, b) => b.Value.CompareTo(a.Value));
        return ordered;
    }

    private static void Walk(DependencyObject? node, System.Action<string, Brush?, DependencyObject> look)
    {
        if (node is null) return;

        switch (node)
        {
            case Border border:
                look("Background", border.Background, node);
                look("BorderBrush", border.BorderBrush, node);
                break;
            case TextBlock text:
                look("Background", text.Background, node);
                look("Foreground", text.Foreground, node);
                break;
            case Panel panel:
                look("Background", panel.Background, node);
                break;
            case Shape shape:
                look("Fill", shape.Fill, node);
                look("Stroke", shape.Stroke, node);
                break;
        }

        int children = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < children; i++)
        {
            Walk(VisualTreeHelper.GetChild(node, i), look);
        }
    }

    private static string Hex(Color colour) => $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";
}
