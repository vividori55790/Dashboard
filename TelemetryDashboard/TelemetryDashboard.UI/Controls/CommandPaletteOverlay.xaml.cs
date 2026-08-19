using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TelemetryDashboard.UI.Services;

namespace TelemetryDashboard.UI.Controls;

public partial class CommandPaletteOverlay : UserControl
{
    private CommandPaletteService? _service;

    public CommandPaletteOverlay()
    {
        InitializeComponent();
    }

    public void AttachService(CommandPaletteService service)
    {
        _service = service;
        RefreshList();
    }

    private void RefreshList()
    {
        if (_service == null) return;
        var query = SearchBox.Text ?? string.Empty;
        CommandListBox.ItemsSource = _service.FilterCommands(query);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshList();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (CommandListBox.SelectedIndex < CommandListBox.Items.Count - 1)
            {
                CommandListBox.SelectedIndex++;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (CommandListBox.SelectedIndex > 0)
            {
                CommandListBox.SelectedIndex--;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (CommandListBox.SelectedItem is string cmdName && _service != null)
            {
                _service.ExecuteCommand(cmdName);
                Visibility = Visibility.Collapsed;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
    }

    private void CommandListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }
}
