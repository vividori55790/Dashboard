using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TelemetryDashboard.UI.Services;

namespace TelemetryDashboard.UI.Controls;

/// <summary>
/// The command palette's window onto <see cref="CommandPaletteService"/>.
/// </summary>
/// <remarks>
/// Every keyboard defect this had came from the same two places. The list was built once at attach
/// time, before any command had been registered, so it opened empty. And nothing ever focused the
/// search box, so what the operator typed went to whatever was behind the palette — which is what
/// "the keys do nothing" looks like from the outside, whatever the key handlers say.
/// <para>
/// The view now holds no state of its own: it asks the service what to show and which row is
/// selected. That was the third defect — the ListBox tracked the selection and the service tracked
/// the filter, so with a query typed they were indexing different lists.
/// </para>
/// </remarks>
public partial class CommandPaletteOverlay : UserControl
{
    private CommandPaletteService? _service;
    private bool _suppressTextChanged;

    /// <summary>Raised when the palette closes, so the window can put focus back.</summary>
    public event Action? OnClosed;

    public CommandPaletteOverlay()
    {
        InitializeComponent();
    }

    public void AttachService(CommandPaletteService service)
    {
        _service = service;
    }

    /// <summary>Shows the palette, listing everything, with the keyboard in the search box.</summary>
    public void Open()
    {
        if (_service is null) return;

        _suppressTextChanged = true;
        SearchBox.Text = string.Empty;
        _suppressTextChanged = false;

        Bind(_service.Open());
        Visibility = Visibility.Visible;

        // At input priority: the control is not arranged at the instant visibility changes, and
        // Focus() on an unrealised element silently does nothing.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => SearchBox.Focus()));
    }

    /// <summary>Hides the palette and tells the service, so the next toggle opens it.</summary>
    public void Close()
    {
        _service?.Close();
        Visibility = Visibility.Collapsed;
        OnClosed?.Invoke();
    }

    /// <summary>Opens or closes, whichever the current state is not.</summary>
    public void Toggle()
    {
        if (Visibility == Visibility.Visible) Close(); else Open();
    }

    private void Bind(System.Collections.Generic.IReadOnlyList<string> items)
    {
        CommandListBox.ItemsSource = items;
        CommandListBox.SelectedIndex = _service?.SelectedIndex ?? -1;
        if (CommandListBox.SelectedItem is not null) CommandListBox.ScrollIntoView(CommandListBox.SelectedItem);
        EmptyNote.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChanged || _service is null) return;
        Bind(_service.ApplyQuery(SearchBox.Text));
    }

    /// <summary>
    /// The palette's keys. Bound to PreviewKeyDown, and that is the whole defect.
    /// </summary>
    /// <remarks>
    /// TextBox registers a class handler for KeyDown that takes the arrow keys for caret movement
    /// and marks them handled. Class handlers run before instance handlers, so an instance
    /// KeyDown="..." on a TextBox is never called for Up or Down — the handler existed, read
    /// correctly, and could not run. Tunnelling through PreviewKeyDown reaches the key first.
    /// <para>
    /// Verified by driving the running window: with KeyDown, pressing Down left the selection on
    /// the first match; with PreviewKeyDown it moves.
    /// </para>
    /// </remarks>
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (_service is null) return;

        switch (e.Key)
        {
            case Key.Down:
                _service.MoveNext();
                CommandListBox.SelectedIndex = _service.SelectedIndex;
                if (CommandListBox.SelectedItem is not null) CommandListBox.ScrollIntoView(CommandListBox.SelectedItem);
                e.Handled = true;
                return;

            case Key.Up:
                _service.MovePrevious();
                CommandListBox.SelectedIndex = _service.SelectedIndex;
                if (CommandListBox.SelectedItem is not null) CommandListBox.ScrollIntoView(CommandListBox.SelectedItem);
                e.Handled = true;
                return;

            case Key.Enter:
                // Closed before the command runs. Several of the registered commands open a modal
                // dialog, and doing that with the palette still on screen leaves it hanging over
                // whatever the operator just asked for.
                string? selected = _service.SelectedCommand;
                if (selected is null) { e.Handled = true; return; }
                Close();
                _service.ExecuteCommand(selected);
                e.Handled = true;
                return;

            case Key.Escape:
                Close();
                e.Handled = true;
                return;
        }
    }

    /// <summary>A click on a row runs it, the same as Enter would.</summary>
    private void CommandListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_service is null || CommandListBox.SelectedItem is not string name) return;
        Close();
        _service.ExecuteCommand(name);
    }

    private void CommandListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // The list is also clickable, so the service has to follow the control here rather than
        // only the other way round.
        if (_service is not null && CommandListBox.SelectedIndex >= 0)
        {
            _service.SelectedIndex = CommandListBox.SelectedIndex;
        }
    }

    /// <summary>A click on the dimmed background closes the palette.</summary>
    private void Scrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender)) Close();
    }
}
