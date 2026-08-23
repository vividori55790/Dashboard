using System;
using System.Collections.Generic;
using System.Linq;

namespace TelemetryDashboard.UI.Services;

/// <summary>
/// The palette's older method names, kept because callers still use them.
/// </summary>
/// <remarks>
/// Every member here forwards to one on the main file and adds nothing. They are together, and
/// labelled, because a second name for an operation is a place a second behaviour can grow: the
/// overlay's Enter key closes the palette before running the command, and
/// <see cref="ExecuteSelected"/> — an alternative Enter that nothing in the product calls — does
/// not. Only tests reach any of these; they are one deliberate sweep away from being deleted along
/// with the tests that keep them alive.
/// </remarks>
public partial class CommandPaletteService
{
    public Dictionary<string, Action?> Commands =>
        _commands.ToDictionary(k => k.Key, v => v.Value.Action, StringComparer.OrdinalIgnoreCase);

    public void Register(string name, Action action) => RegisterCommand(name, "General", action);

    public List<string> Search(string query) => FilterCommands(query);

    public void NavigateNext() => MoveNext();

    public void NavigatePrevious() => MovePrevious();

    public void Execute(string name) => ExecuteCommand(name);

    /// <summary>Runs whatever Enter would run.</summary>
    /// <returns>The command that ran, or null when nothing was selected.</returns>
    public string? ExecuteSelected()
    {
        string? name = SelectedCommand;
        if (name is null) return null;
        ExecuteCommand(name);
        return name;
    }

    public void ToggleVisibility()
    {
        if (IsVisible) Close(); else Open();
    }
}
