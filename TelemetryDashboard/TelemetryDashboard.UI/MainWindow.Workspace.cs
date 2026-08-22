using System;
using System.Linq;
using System.Windows;
using TelemetryDashboard.UI.Docking;

namespace TelemetryDashboard.UI;

/// <summary>
/// Saving and restoring the operator's panel arrangement.
/// </summary>
/// <remarks>
/// The machinery for this shipped complete and disconnected: a serialiser that could write the
/// dock and read it back, a file store that could persist it, and a profile type with a field to
/// carry it — with no caller anywhere. This is the two ends being joined.
/// </remarks>
public partial class MainWindow
{
    /// <summary>Restores the stored arrangement, once the dock has built its panes.</summary>
    /// <remarks>
    /// After Loaded rather than in the constructor. Deserialising into a DockingManager whose
    /// visual tree does not exist yet gives a layout with nothing to attach the content to, and
    /// the panes come back empty — which looks exactly like a restored arrangement until you read
    /// one.
    /// </remarks>
    private void RestoreWorkspace()
    {
        WorkspaceProfile? profile = WorkspaceStore.Load();
        if (profile is null)
        {
            ControlPanel.LogMessage("SYSTEM",
                "No saved workspace; using the arrangement this build ships with.");
            return;
        }

        if (!_layoutManager.LoadLayoutFromXml(profile.LayoutXml))
        {
            // Said out loud. A layout that silently failed to load is indistinguishable from one
            // that was never saved, and the operator would go on rearranging the window every session
            // wondering why it never stuck.
            ControlPanel.LogMessage("SYSTEM",
                $"Saved workspace could not be read ({_layoutManager.LoadFailure}); "
                + "the arrangement this build ships with is in use.");
            return;
        }

        string detail = $"Workspace '{profile.Name}' restored from {WorkspaceStore.DefaultPath}.";
        if (_layoutManager.UnresolvedContentIds.Count > 0)
        {
            detail += " Panes dropped because this build has no such panel: "
                    + string.Join(", ", _layoutManager.UnresolvedContentIds) + ".";
        }

        ControlPanel.LogMessage("SYSTEM", detail);
    }

    /// <summary>Writes the arrangement as it stands.</summary>
    /// <returns>True when it reached disk.</returns>
    private bool SaveWorkspace(string reason)
    {
        string xml = _layoutManager.SaveLayoutToXml();

        // The serialiser answers with this when it has no dock or the write failed. Storing it
        // would replace a good saved arrangement with an empty one at the next clean shutdown.
        if (string.IsNullOrWhiteSpace(xml) || xml.Contains("<AvalonDockLayout></AvalonDockLayout>",
                StringComparison.Ordinal))
        {
            ControlPanel.LogMessage("SYSTEM",
                $"Workspace not saved ({reason}): the dock returned no layout.");
            return false;
        }

        var profile = new WorkspaceProfile
        {
            Name = "Last session",
            ActivePreset = _layoutManager.CurrentPreset.ToString(),
            LayoutXml = xml
        };

        if (WorkspaceStore.Save(profile) is { } failure)
        {
            ControlPanel.LogMessage("SYSTEM", $"Workspace could not be saved: {failure}");
            return false;
        }

        ControlPanel.LogMessage("SYSTEM", $"Workspace saved ({reason}).");
        return true;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // There was no Closing handler at all in this window, which is also why the screen lock
        // could not refuse Alt+F4. This one only saves; it never cancels.
        SaveWorkspace("closing");
    }

    private void BtnSaveWorkspace_Click(object sender, RoutedEventArgs e) => SaveWorkspace("on request");

    private void BtnResetWorkspace_Click(object sender, RoutedEventArgs e)
    {
        bool removed = WorkspaceStore.Clear();
        ControlPanel.LogMessage("SYSTEM", removed
            ? "Saved workspace cleared. The arrangement this build ships with returns on the next start."
            : "There was no saved workspace to clear.");
    }
}
