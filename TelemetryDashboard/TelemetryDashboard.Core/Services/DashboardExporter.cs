using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Services;

public class DashboardExporter : IDashboardExporter
{
    public string ExportCustomHtmlDashboard(string targetFilePath, string title, IEnumerable<WidgetConfig>? widgets = null)
    {
        var widgetList = widgets?.ToList();
        if (widgetList == null || widgetList.Count == 0)
        {
            widgetList = new List<WidgetConfig>
            {
                new WidgetConfig { Id = "w-temp", WidgetType = "digital_card", Title = "Edge Temp Sensor (CH-1)", Field = "temp", Unit = "°C", ColorTheme = "#66FCF1" },
                new WidgetConfig { Id = "w-vib", WidgetType = "digital_card", Title = "Vibration Accelerometer (CH-2)", Field = "vibration", Unit = "g", ColorTheme = "#BA68C8" },
                new WidgetConfig { Id = "w-vin", WidgetType = "gauge_meter", Title = "Primary Bus Voltage (CH-3)", Field = "vin", Unit = "V", MinLimit = 0, MaxLimit = 500, ColorTheme = "#00FF66" },
                new WidgetConfig { Id = "w-zscore", WidgetType = "zscore_card", Title = "System ML Z-Score Engine", Field = "anomalyScore", Unit = "σ", ColorTheme = "#FF2E63" },
                new WidgetConfig { Id = "w-chart", WidgetType = "line_chart", Title = "Real-Time Telemetry Waveform", Field = "temp", Unit = "°C", ColorTheme = "#66FCF1" }
            };
        }

        string widgetsJson = JsonSerializer.Serialize(widgetList, new JsonSerializerOptions { WriteIndented = true });

        string templateHtml = $@"<!DOCTYPE html>
<html lang=""ko"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{title}</title>
    <link rel=""preconnect"" href=""https://fonts.googleapis.com"">
    <link rel=""preconnect"" href=""https://fonts.gstatic.com"" crossorigin>
    <link href=""https://fonts.googleapis.com/css2?family=Inter:wght@400;600;700;800&family=JetBrains+Mono:wght@400;700&display=swap"" rel=""stylesheet"">
    <style>
        :root {{
            --bg: #0B0C10;
            --surface: #1F2833;
            --card: #141A22;
            --accent: #66FCF1;
            --accent-glow: 0 0 15px rgba(102, 252, 241, 0.4);
            --danger: #FF2E63;
            --text: #C5C6C7;
            --text-bright: #FFFFFF;
        }}

        * {{ box-sizing: border-box; margin: 0; padding: 0; }}
        body {{
            background-color: var(--bg);
            color: var(--text);
            font-family: 'Inter', sans-serif;
            padding: 24px;
            min-height: 100vh;
        }}

        header {{
            display: flex;
            justify-content: space-between;
            align-items: center;
            padding-bottom: 20px;
            border-bottom: 1px solid #2C3540;
            margin-bottom: 24px;
        }}

        h1 {{
            font-size: 22px;
            font-weight: 800;
            color: var(--text-bright);
            display: flex;
            align-items: center;
            gap: 10px;
        }}

        h1 span {{ color: var(--accent); }}

        .chip {{
            background: #141A22;
            border: 1px solid #2C3540;
            padding: 6px 14px;
            border-radius: 20px;
            font-size: 12px;
            font-weight: 600;
            color: var(--accent);
            display: flex;
            align-items: center;
            gap: 8px;
        }}

        .dot {{
            width: 8px; height: 8px; border-radius: 50%;
            background: var(--accent); box-shadow: var(--accent-glow);
        }}

        .dashboard-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
            gap: 20px;
        }}

        .widget-card {{
            background: var(--card);
            border: 1px solid #2C3540;
            border-radius: 12px;
            padding: 20px;
            position: relative;
            box-shadow: 0 4px 20px rgba(0, 0, 0, 0.3);
            display: flex;
            flex-direction: column;
            justify-content: space-between;
        }}

        .widget-card:hover {{ border-color: var(--accent); }}

        .widget-title {{
            font-size: 12px;
            font-weight: 700;
            color: #8892B0;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            margin-bottom: 8px;
        }}

        .widget-value {{
            font-family: 'JetBrains Mono', monospace;
            font-size: 36px;
            font-weight: 700;
            color: var(--text-bright);
        }}

        .widget-unit {{
            font-size: 14px;
            color: var(--accent);
            margin-left: 4px;
        }}

        .widget-footer {{
            margin-top: 12px;
            font-size: 11px;
            color: #8892B0;
            display: flex;
            justify-content: space-between;
        }}

        .z-score-tag {{
            background: rgba(102, 252, 241, 0.1);
            color: var(--accent);
            padding: 2px 8px;
            border-radius: 4px;
            font-weight: bold;
        }}

        .z-score-tag.anomaly {{
            background: rgba(255, 46, 99, 0.2);
            color: var(--danger);
            box-shadow: 0 0 10px rgba(255, 46, 99, 0.5);
        }}

        .gauge-bar-bg {{
            background: #1B2430;
            height: 10px;
            border-radius: 5px;
            overflow: hidden;
            margin-top: 10px;
        }}

        .gauge-bar-fill {{
            height: 100%;
            width: 0%;
            background: var(--accent);
            border-radius: 5px;
            transition: width 0.3s ease;
        }}

        canvas.widget-chart {{
            width: 100%;
            height: 120px;
            background: #0B0C10;
            border-radius: 6px;
            margin-top: 10px;
        }}
    </style>
</head>
<body>

    <header>
        <h1>🚀 <span>{title}</span></h1>
        <div class=""chip"">
            <div class=""dot""></div>
            <span id=""conn-status"">WS CONNECTED (:8080)</span>
        </div>
    </header>

    <main class=""dashboard-grid"" id=""dashboard-container"">
        <!-- Rendered dynamically from widgets schema -->
    </main>

    <script src=""http://localhost:8080/telemetry-client.js""></script>
    <script>
        const widgetConfigs = {widgetsJson};
        const chartBuffers = {{}};

        function buildDashboard() {{
            const container = document.getElementById('dashboard-container');
            container.innerHTML = '';

            widgetConfigs.forEach(w => {{
                const card = document.createElement('div');
                card.className = 'widget-card';
                card.id = `card-${{w.Id}}`;

                let innerHtml = `<div class=""widget-title"">${{w.Title}}</div>`;

                if (w.WidgetType === 'digital_card') {{
                    innerHtml += `
                        <div class=""widget-value"" id=""val-${{w.Id}}"" style=""color: ${{w.ColorTheme}}"">--<span class=""widget-unit"">${{w.Unit}}</span></div>
                        <div class=""widget-footer"">
                            <span>Field: ${{w.Field}}</span>
                            <span>Live Telemetry</span>
                        </div>`;
                }} else if (w.WidgetType === 'gauge_meter') {{
                    innerHtml += `
                        <div class=""widget-value"" id=""val-${{w.Id}}"" style=""color: ${{w.ColorTheme}}"">--<span class=""widget-unit"">${{w.Unit}}</span></div>
                        <div class=""gauge-bar-bg"">
                            <div class=""gauge-bar-fill"" id=""gauge-${{w.Id}}"" style=""background-color: ${{w.ColorTheme}}""></div>
                        </div>
                        <div class=""widget-footer"">
                            <span>Min: ${{w.MinLimit}} ${{w.Unit}}</span>
                            <span>Max: ${{w.MaxLimit}} ${{w.Unit}}</span>
                        </div>`;
                }} else if (w.WidgetType === 'zscore_card') {{
                    innerHtml += `
                        <div class=""widget-value"" id=""val-${{w.Id}}"" style=""color: ${{w.ColorTheme}}"">0.0<span class=""widget-unit"">${{w.Unit}}</span></div>
                        <div class=""widget-footer"">
                            <span>Status: <strong id=""zs-status-${{w.Id}}"" class=""z-score-tag"">NORMAL</strong></span>
                            <span>ML Z-Score Engine</span>
                        </div>`;
                }} else if (w.WidgetType === 'line_chart') {{
                    chartBuffers[w.Id] = [];
                    innerHtml += `
                        <div class=""widget-value"" id=""val-${{w.Id}}"" style=""font-size: 20px; color: ${{w.ColorTheme}}"">-- ${{w.Unit}}</div>
                        <canvas class=""widget-chart"" id=""chart-${{w.Id}}""></canvas>
                        <div class=""widget-footer"">
                            <span>Real-time Trend (${{w.Field}})</span>
                            <span>30 Samples</span>
                        </div>`;
                }}

                card.innerHTML = innerHtml;
                container.appendChild(card);
            }});
        }}

        function drawSparkline(canvasId, data, color) {{
            const canvas = document.getElementById(canvasId);
            if (!canvas) return;
            const ctx = canvas.getContext('2d');
            const w = canvas.width = canvas.parentElement.clientWidth;
            const h = canvas.height = 120;

            ctx.clearRect(0, 0, w, h);
            if (data.length < 2) return;

            let min = Math.min(...data);
            let max = Math.max(...data);
            if (min === max) {{ min -= 1; max += 1; }}

            ctx.strokeStyle = color;
            ctx.lineWidth = 2;
            ctx.beginPath();

            const step = w / (data.length - 1);
            data.forEach((val, i) => {{
                const x = i * step;
                const y = h - ((val - min) / (max - min)) * (h - 20) - 10;
                if (i === 0) ctx.moveTo(x, y);
                else ctx.lineTo(x, y);
            }});
            ctx.stroke();
        }}

        window.addEventListener('DOMContentLoaded', () => {{
            buildDashboard();

            if (typeof TelemetryClient !== 'undefined') {{
                TelemetryClient.connect('ws://localhost:8080/ws');
                TelemetryClient.onData(data => {{
                    widgetConfigs.forEach(w => {{
                        const rawVal = data[w.Field] !== undefined ? data[w.Field] : (data.temp || 0);
                        const val = typeof rawVal === 'number' ? rawVal : parseFloat(rawVal) || 0;

                        const valEl = document.getElementById(`val-${{w.Id}}`);
                        if (valEl) {{
                            valEl.innerHTML = (w.WidgetType === 'zscore_card' ? val.toFixed(1) : val.toFixed(2)) + `<span class=""widget-unit"">${{w.Unit}}</span>`;
                        }}

                        if (w.WidgetType === 'gauge_meter') {{
                            const gauge = document.getElementById(`gauge-${{w.Id}}`);
                            if (gauge) {{
                                const pct = Math.min(100, Math.max(0, ((val - w.MinLimit) / (w.MaxLimit - w.MinLimit)) * 100));
                                gauge.style.width = pct + '%';
                            }}
                        }} else if (w.WidgetType === 'zscore_card') {{
                            const tag = document.getElementById(`zs-status-${{w.Id}}`);
                            if (tag) {{
                                if (val >= 3.5) {{
                                    tag.innerText = 'CRITICAL ANOMALY';
                                    tag.className = 'z-score-tag anomaly';
                                }} else if (val >= 2.0) {{
                                    tag.innerText = 'WARNING';
                                    tag.className = 'z-score-tag anomaly';
                                }} else {{
                                    tag.innerText = 'NORMAL';
                                    tag.className = 'z-score-tag';
                                }}
                            }}
                        }} else if (w.WidgetType === 'line_chart') {{
                            const buf = chartBuffers[w.Id];
                            if (buf) {{
                                buf.push(val);
                                if (buf.length > 30) buf.shift();
                                drawSparkline(`chart-${{w.Id}}`, buf, w.ColorTheme);
                            }}
                        }}
                    }});
                }});
            }}
        }});
    </script>
</body>
</html>";
        File.WriteAllText(targetFilePath, templateHtml);
        return targetFilePath;
    }
}
