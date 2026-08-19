using System.Collections.Generic;

namespace TelemetryDashboard.UI.Docking;

public class WorkspaceLayoutState
{
    public bool IsInitialized { get; set; } = true;
    public string ActivePreset { get; set; } = "ScopeMode";
    public List<string> VisiblePanels { get; } = new();
    public List<string> FloatingPanels { get; } = new();

    public void LoadPreset(string presetName)
    {
        ActivePreset = presetName;
        VisiblePanels.Clear();
        switch (presetName)
        {
            case "ScopeMode":
                VisiblePanels.Add("ScopeView");
                break;
            case "3DTwinMode":
            case "Twin3DMode":
                VisiblePanels.Add("Twin3DView");
                break;
            case "ControlPanelMode":
                VisiblePanels.Add("ControlPanel");
                break;
            default:
                VisiblePanels.Add("ScopeView");
                break;
        }
    }

    public void ToggleFloating(string panelName, bool isFloating)
    {
        if (isFloating)
        {
            if (!FloatingPanels.Contains(panelName))
            {
                FloatingPanels.Add(panelName);
            }
        }
        else
        {
            FloatingPanels.Remove(panelName);
        }
    }
}
