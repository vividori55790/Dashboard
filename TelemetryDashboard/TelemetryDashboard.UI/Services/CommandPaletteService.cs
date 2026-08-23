using System;
using System.Collections.Generic;
using System.Linq;

namespace TelemetryDashboard.UI.Services;

public class CommandItem
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public Action? Action { get; set; }
}

/// <summary>
/// The commands the palette can run, which of them the current query matches, and which one is
/// selected.
/// </summary>
/// <remarks>
/// The selection used to live in the ListBox and the filtering here, which meant the two disagreed:
/// <c>NavigateNext</c> moved an index bounded by the <em>total</em> command count while the list on
/// screen showed the filtered subset, so with a query typed the arrow keys ran off the end of what
/// was visible. Keeping both here makes the behaviour one thing, and one that a test can drive
/// without a window.
/// <para>
/// Filtering is a plain case-insensitive substring test. It was a regex over an escaped query,
/// which is the same test written so that it looks like it supports patterns and does not.
/// </para>
/// </remarks>
public partial class CommandPaletteService
{
    private readonly Dictionary<string, CommandItem> _commands = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _filtered = new();
    private int _selectedIndex;

    public bool IsVisible { get; private set; }

    /// <summary>Commands matching the current query, in registration order.</summary>
    public IReadOnlyList<string> Filtered => _filtered;

    /// <summary>Index into <see cref="Filtered"/>, or -1 when nothing matches.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set => _selectedIndex = _filtered.Count == 0 ? -1 : Math.Clamp(value, 0, _filtered.Count - 1);
    }

    /// <summary>The command Enter would run, or null when nothing matches.</summary>
    public string? SelectedCommand =>
        _selectedIndex >= 0 && _selectedIndex < _filtered.Count ? _filtered[_selectedIndex] : null;

    public void RegisterCommand(string name, string category, Action? action)
    {
        _commands[name] = new CommandItem { Name = name, Category = category, Action = action };
    }

    /// <summary>Commands whose name or category contains <paramref name="query"/>.</summary>
    /// <remarks>
    /// The category counts because it is the ribbon tab the command sits on, and that is often what
    /// somebody remembers: they know the export lives under 도구 without remembering what the
    /// button is called. Matching names alone made the tab name -- the one piece of structure the
    /// palette inherited -- unsearchable.
    /// </remarks>
    public List<string> FilterCommands(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return _commands.Keys.ToList();

        string needle = query.Trim();
        return _commands.Values
            .Where(c => c.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                     || c.Category.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Name)
            .ToList();
    }

    /// <summary>
    /// Applies a query and puts the selection on the first match.
    /// </summary>
    /// <remarks>
    /// On the first match rather than on nothing, so typing part of a command and pressing Enter
    /// runs it. Left unselected, the palette's whole reason for existing — reaching a command by
    /// typing — took a redundant Down press that nothing told the operator about.
    /// </remarks>
    public IReadOnlyList<string> ApplyQuery(string? query)
    {
        _filtered = FilterCommands(query);
        _selectedIndex = _filtered.Count == 0 ? -1 : 0;
        return _filtered;
    }

    /// <summary>Moves down the visible list, wrapping at the end.</summary>
    public void MoveNext()
    {
        if (_filtered.Count == 0) { _selectedIndex = -1; return; }
        _selectedIndex = (_selectedIndex + 1) % _filtered.Count;
    }

    /// <summary>Moves up the visible list, wrapping at the start.</summary>
    public void MovePrevious()
    {
        if (_filtered.Count == 0) { _selectedIndex = -1; return; }
        _selectedIndex = (_selectedIndex - 1 + _filtered.Count) % _filtered.Count;
    }

    public void ExecuteCommand(string? name)
    {
        if (name is not null && _commands.TryGetValue(name, out CommandItem? cmd)) cmd.Action?.Invoke();
    }

    /// <summary>
    /// Opens the palette with an empty query and every command listed.
    /// </summary>
    /// <remarks>
    /// The list used to be built once, when the overlay was attached — which happened before the
    /// commands were registered, so it was built from an empty dictionary and stayed empty until
    /// the operator typed something. Opening the palette showed nothing at all. Building it here,
    /// on open, makes the order the two are wired in stop mattering.
    /// </remarks>
    public IReadOnlyList<string> Open()
    {
        IsVisible = true;
        return ApplyQuery(null);
    }

    /// <summary>Closes the palette, and records that it is closed.</summary>
    /// <remarks>
    /// Escape and Enter used to hide the control directly and leave <see cref="IsVisible"/> true,
    /// so the next Ctrl+Shift+P toggled it back to false and the palette did not appear. It took
    /// two presses to reopen, every time, after any use of it.
    /// </remarks>
    public void Close() => IsVisible = false;

}
