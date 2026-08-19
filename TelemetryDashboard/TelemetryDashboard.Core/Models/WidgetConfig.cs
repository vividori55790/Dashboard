using System;

namespace TelemetryDashboard.Core.Models;

public class WidgetConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WidgetType { get; set; } = "digital_card"; // digital_card, line_chart, gauge_meter, zscore_card
    public string Title { get; set; } = "Telemetry Field";
    public string Field { get; set; } = "temp";
    public string Unit { get; set; } = "°C";
    public double MinLimit { get; set; } = 0.0;
    public double MaxLimit { get; set; } = 100.0;
    public string ColorTheme { get; set; } = "#66FCF1";
    public int ColumnSpan { get; set; } = 1;
    public int RowSpan { get; set; } = 1;
}
