using System.Collections.Generic;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Interfaces;

public interface IDashboardExporter
{
    /// <summary>Writes a standalone dashboard that connects back to a host on <paramref name="port"/>.</summary>
    /// <remarks>
    /// The port is a parameter because the page has to say where it is going, and the exporter is
    /// not the thing that knows. It was written into the template as the literal 8080 -- in the
    /// script tag, in the WebSocket URL and in the connection chip -- so a host started on any
    /// other port exported a page that pointed at nothing, and did so while claiming to be
    /// connected.
    /// </remarks>
    string ExportCustomHtmlDashboard(
        string targetFilePath, string title, IEnumerable<WidgetConfig>? widgets = null, int port = 8080);
}
