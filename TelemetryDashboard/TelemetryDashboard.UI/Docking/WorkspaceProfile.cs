namespace TelemetryDashboard.UI.Docking;

public class WorkspaceProfile
{
    public string Name { get; set; } = "Default";
    public string ProfileName
    {
        get => Name;
        set => Name = value;
    }
    public string ActivePreset { get; set; } = "ScopeMode";
    public int PanelWidth { get; set; } = 300;
    public int PanelHeight { get; set; } = 500;
    public string LayoutXml { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
}
