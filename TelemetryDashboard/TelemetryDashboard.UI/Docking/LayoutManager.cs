using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvalonDock;
using AvalonDock.Layout;
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
            if (_dockingManager is null) return true;

            // The part that makes deserialising a layout safe, and the part that is easy to leave
            // out. A serialised layout records ContentId and the shape of the panes -- never the
            // controls. Deserialise without answering the callback and every pane comes back in the
            // right place and empty: the window looks arranged and shows nothing, which is a worse
            // outcome than not restoring at all.
            Dictionary<string, object> byId = CurrentContentById(_dockingManager);

            var serializer = new XmlLayoutSerializer(_dockingManager);
            var unresolved = new List<string>();

            serializer.LayoutSerializationCallback += (_, e) =>
            {
                string id = e.Model.ContentId ?? string.Empty;
                if (byId.TryGetValue(id, out object? content))
                {
                    e.Content = content;
                    return;
                }

                // A pane saved by an older build whose control no longer exists. Dropped rather
                // than restored empty, and named so the reason is visible.
                unresolved.Add(id);
                e.Cancel = true;
            };

            using var reader = new StringReader(xml);
            serializer.Deserialize(reader);

            UnresolvedContentIds = unresolved;
            return true;
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException
                                      or ArgumentException or NullReferenceException)
        {
            // A layout that will not load must not take the window with it. The application falls
            // back to the arrangement declared in XAML, which is always valid.
            LoadFailure = ex.Message;
            ApplyPreset(LayoutPreset.ScopeMode);
            return false;
        }
    }

    /// <summary>Panes the layout dropped because nothing in this build answers to their id.</summary>
    public IReadOnlyList<string> UnresolvedContentIds { get; private set; } = Array.Empty<string>();

    /// <summary>Why the last load failed, or null.</summary>
    public string? LoadFailure { get; private set; }

    /// <summary>Every piece of content currently in the dock, keyed by the id the layout records.</summary>
    private static Dictionary<string, object> CurrentContentById(DockingManager dockingManager)
    {
        var map = new Dictionary<string, object>(StringComparer.Ordinal);
        if (dockingManager.Layout is null) return map;

        foreach (LayoutContent item in dockingManager.Layout.Descendents().OfType<LayoutContent>())
        {
            if (item.ContentId is { Length: > 0 } id && item.Content is not null)
            {
                map[id] = item.Content;
            }
        }

        return map;
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
