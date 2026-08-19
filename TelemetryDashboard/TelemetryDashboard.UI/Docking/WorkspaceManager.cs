using System;
using System.IO;
using System.Text;

namespace TelemetryDashboard.UI.Docking;

public class WorkspaceManager
{
    public WorkspaceProfile LoadWorkspaceProfile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Workspace profile file not found", filePath);
        }

        string content = File.ReadAllText(filePath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(content))
        {
            return new WorkspaceProfile { Name = "Default" };
        }

        try
        {
            return LayoutProfileSerializer.LoadFromXml(content);
        }
        catch
        {
            return new WorkspaceProfile { Name = "Default" };
        }
    }

    public void SaveWorkspaceProfile(WorkspaceProfile profile, string filePath)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be empty", nameof(filePath));
        }

        string directory = Path.GetDirectoryName(filePath)!;
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string xml = LayoutProfileSerializer.SaveToXml(profile);
        File.WriteAllText(filePath, xml, Encoding.UTF8);
    }
}
