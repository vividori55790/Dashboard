using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace TelemetryDashboard.UI.Docking;

public static class LayoutProfileSerializer
{
    public static string SaveToXml(WorkspaceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var xml = new XElement("WorkspaceProfile",
            new XElement("Name", profile.Name),
            new XElement("ActivePreset", profile.ActivePreset),
            new XElement("PanelWidth", profile.PanelWidth),
            new XElement("PanelHeight", profile.PanelHeight),
            new XElement("LayoutXml", profile.LayoutXml),
            new XElement("Version", profile.Version)
        );
        return xml.ToString();
    }

    public static WorkspaceProfile LoadFromXml(string xmlOrJson)
    {
        if (string.IsNullOrWhiteSpace(xmlOrJson))
        {
            return new WorkspaceProfile { Name = "Default" };
        }

        try
        {
            // Try XML first
            if (xmlOrJson.TrimStart().StartsWith("<"))
            {
                var doc = XDocument.Parse(xmlOrJson);
                var root = doc.Root;
                if (root == null) return new WorkspaceProfile { Name = "Default" };

                return new WorkspaceProfile
                {
                    Name = root.Element("Name")?.Value ?? root.Element("ProfileName")?.Value ?? "Default",
                    ActivePreset = root.Element("ActivePreset")?.Value ?? "ScopeMode",
                    PanelWidth = int.TryParse(root.Element("PanelWidth")?.Value, out var w) ? w : 300,
                    PanelHeight = int.TryParse(root.Element("PanelHeight")?.Value, out var h) ? h : 500,
                    LayoutXml = root.Element("LayoutXml")?.Value ?? string.Empty,
                    Version = root.Element("Version")?.Value ?? "1.0"
                };
            }

            // Try JSON fallback
            if (xmlOrJson.TrimStart().StartsWith("{"))
            {
                using var jsonDoc = JsonDocument.Parse(xmlOrJson);
                var root = jsonDoc.RootElement;

                var profile = new WorkspaceProfile();
                if (root.TryGetProperty("name", out var n) || root.TryGetProperty("Name", out n)) profile.Name = n.GetString() ?? "Default";
                if (root.TryGetProperty("preset", out var p) || root.TryGetProperty("ActivePreset", out p)) profile.ActivePreset = p.GetString() ?? "ScopeMode";
                if (root.TryGetProperty("panelWidth", out var pw) || root.TryGetProperty("PanelWidth", out pw)) profile.PanelWidth = pw.GetInt32();
                if (root.TryGetProperty("panelHeight", out var ph) || root.TryGetProperty("PanelHeight", out ph)) profile.PanelHeight = ph.GetInt32();
                if (root.TryGetProperty("layoutXml", out var lx) || root.TryGetProperty("LayoutXml", out lx)) profile.LayoutXml = lx.GetString() ?? string.Empty;
                if (root.TryGetProperty("version", out var v) || root.TryGetProperty("Version", out v)) profile.Version = v.GetString() ?? "1.0";

                return profile;
            }

            return new WorkspaceProfile { Name = "Default" };
        }
        catch
        {
            return new WorkspaceProfile { Name = "Default" };
        }
    }

    public static void SaveToStream(WorkspaceProfile profile, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(stream);

        byte[] bytes = Encoding.UTF8.GetBytes(SaveToXml(profile));
        stream.Write(bytes, 0, bytes.Length);
    }
}
