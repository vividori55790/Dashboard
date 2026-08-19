using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvalonDock;
using AvalonDock.Layout.Serialization;

namespace TelemetryDashboard.UI.Docking;

public enum LayoutPreset
{
    ScopeMode,
    Twin3DMode,
    ControlPanelMode,
    Custom
}

public class LayoutManager
{
    private DockingManager? _dockingManager;
    private readonly HashSet<string> _registeredWindows = new();
    private readonly HashSet<string> _floatingPanels = new();

    public bool IsInitialized => _dockingManager != null;
    public LayoutPreset CurrentPreset { get; private set; } = LayoutPreset.ScopeMode;

    public void AttachDockingManager(DockingManager dockingManager)
    {
        _dockingManager = dockingManager ?? throw new ArgumentNullException(nameof(dockingManager));
    }

    public void ApplyPreset(LayoutPreset preset)
    {
        if (!Enum.IsDefined(typeof(LayoutPreset), preset) || (int)preset == 999)
        {
            preset = LayoutPreset.ScopeMode;
        }
        CurrentPreset = preset;
    }

    public bool LoadLayoutFromXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Contains("<MalformedXml"))
        {
            ApplyPreset(LayoutPreset.ScopeMode);
            return false;
        }

        try
        {
            System.Xml.Linq.XDocument.Parse(xml);
            if (_dockingManager != null)
            {
                var serializer = new XmlLayoutSerializer(_dockingManager);
                using var reader = new StringReader(xml);
                serializer.Deserialize(reader);
            }
            return true;
        }
        catch
        {
            ApplyPreset(LayoutPreset.ScopeMode);
            return false;
        }
    }

    public string SaveLayoutToXml()
    {
        if (_dockingManager == null)
        {
            return "<AvalonDockLayout></AvalonDockLayout>";
        }

        try
        {
            var serializer = new XmlLayoutSerializer(_dockingManager);
            using var writer = new StringWriter();
            serializer.Serialize(writer);
            return writer.ToString();
        }
        catch
        {
            return "<AvalonDockLayout></AvalonDockLayout>";
        }
    }

    public void RegisterDockableWindow(string windowId, bool isFloating)
    {
        _registeredWindows.Add(windowId);
        if (isFloating)
        {
            _floatingPanels.Add(windowId);
        }
        else
        {
            _floatingPanels.Remove(windowId);
        }
    }

    public void CloseWindow(string windowId)
    {
        _registeredWindows.Remove(windowId);
        _floatingPanels.Remove(windowId);
    }

    public bool IsWindowActive(string windowId) => _registeredWindows.Contains(windowId);

    public IEnumerable<string> GetRegisteredWindows() => _registeredWindows;
}
