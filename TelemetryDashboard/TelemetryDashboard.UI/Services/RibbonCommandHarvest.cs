using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace TelemetryDashboard.UI.Services;

/// <summary>
/// Reads the ribbon and turns every command on it into a palette entry.
/// </summary>
/// <remarks>
/// The palette worked and knew about five things. The ribbon carries roughly forty across ten tabs,
/// and a command on an unselected tab cannot be seen at all — which is the reason to have a palette
/// and exactly what it was failing to solve. Registering the other thirty-five by hand would have
/// worked once; the thirty-sixth button somebody adds would not be in it, and nothing would say so.
/// <para>
/// The walk is over the <em>logical</em> tree, which is what makes this possible at start-up: a
/// TabControl does not build the visual tree of an unselected tab, so a visual walk finds only the
/// tab that happens to be showing. The logical children exist as soon as the XAML is parsed.
/// </para>
/// </remarks>
public static class RibbonCommandHarvest
{
    /// <summary>Shortest caption that is a label rather than an icon glyph.</summary>
    /// <remarks>
    /// Icon-only TextBlocks carry a single Segoe MDL2 code point, and joining those into a command
    /// name produces entries nobody can search for. Two characters is enough to keep a real caption
    /// while dropping every glyph.
    /// </remarks>
    public const int ShortestCaption = 2;

    /// <summary>Every clickable command on <paramref name="ribbon"/>, tab by tab.</summary>
    public static IReadOnlyList<CommandItem> From(TabControl? ribbon)
    {
        var found = new List<CommandItem>();
        if (ribbon is null) return found;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (object? item in ribbon.Items)
        {
            if (item is not TabItem tab) continue;

            string category = tab.Header?.ToString()?.Trim() ?? "Ribbon";
            foreach (ButtonBase button in Buttons(tab))
            {
                if (CaptionOf(button) is not { Length: > 0 } caption) continue;
                if (!seen.Add(caption)) continue;

                // Raising Click rather than calling the handler: the handler is private to the
                // window, and the button is the thing the ribbon already invokes.
                ButtonBase target = button;
                found.Add(new CommandItem
                {
                    Name = caption,
                    Category = category,
                    Action = () => target.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent))
                });
            }
        }

        return found;
    }

    /// <summary>The caption an operator would search for, or null when the button has none.</summary>
    private static string? CaptionOf(ButtonBase button)
    {
        if (button.Content is string direct) return direct.Trim();

        var words = new List<string>();
        foreach (DependencyObject child in Descendants(button))
        {
            if (child is TextBlock text && text.Text?.Trim() is { Length: >= ShortestCaption } word)
            {
                words.Add(word);
            }
        }

        return words.Count == 0 ? null : string.Join(" ", words);
    }

    private static IEnumerable<ButtonBase> Buttons(DependencyObject root)
    {
        foreach (DependencyObject child in Descendants(root))
        {
            if (child is ButtonBase button) yield return button;
        }
    }

    /// <summary>Every logical descendant of <paramref name="root"/>, depth first.</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (object? child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject node) continue;

            yield return node;
            foreach (DependencyObject deeper in Descendants(node)) yield return deeper;
        }
    }
}
