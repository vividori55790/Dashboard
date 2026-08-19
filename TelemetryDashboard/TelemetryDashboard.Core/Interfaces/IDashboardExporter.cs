using System.Collections.Generic;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Interfaces;

public interface IDashboardExporter
{
    string ExportCustomHtmlDashboard(string targetFilePath, string title, IEnumerable<WidgetConfig>? widgets = null);
}
