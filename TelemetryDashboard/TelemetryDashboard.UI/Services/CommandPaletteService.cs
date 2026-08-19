using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TelemetryDashboard.UI.Services;

public class CommandItem
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public Action? Action { get; set; }
}

public class CommandPaletteService
{
    private readonly Dictionary<string, CommandItem> _commands = new(StringComparer.OrdinalIgnoreCase);

    public bool IsVisible { get; private set; } = false;
    public int SelectedIndex { get; set; } = 0;
    public Dictionary<string, Action?> Commands => _commands.ToDictionary(k => k.Key, v => v.Value.Action, StringComparer.OrdinalIgnoreCase);

    public void RegisterCommand(string name, string category, Action? action)
    {
        _commands[name] = new CommandItem { Name = name, Category = category, Action = action };
    }

    public void Register(string name, Action action)
    {
        RegisterCommand(name, "General", action);
    }

    public List<string> FilterCommands(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return _commands.Keys.ToList();
        }

        string escapedQuery = Regex.Escape(query);
        return _commands.Keys
            .Where(k => Regex.IsMatch(k, escapedQuery, RegexOptions.IgnoreCase))
            .ToList();
    }

    public List<string> Search(string query) => FilterCommands(query);

    public void ExecuteCommand(string name)
    {
        if (name != null && _commands.TryGetValue(name, out var cmd))
        {
            cmd.Action?.Invoke();
        }
    }

    public void Execute(string name) => ExecuteCommand(name);

    public void ToggleVisibility()
    {
        IsVisible = !IsVisible;
    }

    public void NavigateNext()
    {
        if (_commands.Count > 0 && SelectedIndex < _commands.Count - 1)
        {
            SelectedIndex++;
        }
    }

    public void NavigatePrevious()
    {
        if (SelectedIndex > 0)
        {
            SelectedIndex--;
        }
    }
}
