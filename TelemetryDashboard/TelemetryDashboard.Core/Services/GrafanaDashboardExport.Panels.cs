using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Ingest;

namespace TelemetryDashboard.Core.Services;

/// <summary>
/// How the channels are laid out, and what a panel looks like.
/// </summary>
/// <remarks>
/// Kept to the documented minimum. Grafana fills every field a panel omits with a default and
/// migrates the schema forward on import, so a spartan panel imports into more versions than a
/// rich one — and a dashboard that fails to import is worth nothing at all.
/// </remarks>
public static partial class GrafanaDashboardExport
{
    /// <summary>Grafana's grid is 24 columns wide; two panels a line.</summary>
    private const int PanelWidth = 12;
    private const int PanelHeight = 8;

    /// <summary>
    /// Rows of channels, one row per port, two panels wide.
    /// </summary>
    /// <remarks>
    /// Port is the grouping because it is the one this host can defend: it is the thing an operator
    /// can unplug, and it is what /api/inputs already groups by. W3 asks for the W1 taxonomy —
    /// quantity kind and subsystem — and W1 is not built, so grouping by anything more meaningful
    /// than the cable would mean inferring a subsystem from a channel name. That is precisely the
    /// guess W1 says to answer "unclassified" to instead.
    /// <para>
    /// One panel per channel rather than one per unit. Putting a 400 V bus and a 5 V rail on one
    /// axis because they share a unit makes the second a flat line along the bottom of the plot,
    /// which is the "quiet channel" rendering this product refuses everywhere else.
    /// </para>
    /// </remarks>
    private static object[] Layout(IReadOnlyList<InputChannel> reporting)
    {
        var panels = new List<object>();
        int id = 1;
        int y = 0;

        foreach (IGrouping<string, InputChannel> port in reporting
            .GroupBy(c => c.Port, StringComparer.OrdinalIgnoreCase))
        {
            panels.Add(RowPanel(id++, port.Key, port.Count(), y));
            y++;

            int column = 0;
            foreach (InputChannel channel in port)
            {
                panels.Add(TimeseriesPanel(id++, channel, column * PanelWidth, y));
                column++;
                if (column == 2) { column = 0; y += PanelHeight; }
            }

            if (column != 0) y += PanelHeight;
        }

        return panels.ToArray();
    }

    private static object RowPanel(int id, string port, int channels, int y) => new
    {
        id,
        type = "row",
        title = $"{port} · {channels}",
        collapsed = false,
        gridPos = new { h = 1, w = 24, x = 0, y },
        panels = Array.Empty<object>()
    };

    /// <summary>One channel, one graph.</summary>
    private static object TimeseriesPanel(int id, InputChannel channel, int x, int y)
    {
        string rawUnit = (channel.Unit ?? string.Empty).Trim();

        return new
        {
            id,
            type = "timeseries",
            title = rawUnit.Length > 0 ? $"{channel.Channel} [{rawUnit}]" : channel.Channel,

            // Provenance on the panel itself, because a chart lifted into a report loses whatever
            // the console knew about where it came from. ARCHITECTURE §7's argument for keeping a
            // number attributable applies to a picture of the number too.
            description = $"port {channel.Port} · node {channel.NodeId}" + UnitNote(rawUnit),
            datasource = DatasourceRef(),
            gridPos = new { h = PanelHeight, w = PanelWidth, x, y },
            fieldConfig = new
            {
                defaults = new
                {
                    unit = UnitId(rawUnit),
                    color = new { mode = "palette-classic" },
                    custom = new
                    {
                        drawStyle = "line",
                        lineInterpolation = "linear",
                        lineWidth = 1,
                        fillOpacity = 0,
                        showPoints = "auto",
                        axisPlacement = "auto",

                        // The single most important field on this panel. With spanNulls on,
                        // Grafana draws a straight line across a gap where the hub received
                        // nothing, so an outage renders as a calm, plausible trend -- the exact
                        // failure ARCHITECTURE opens with, reproduced in somebody else's tool.
                        spanNulls = false
                    },
                    mappings = Array.Empty<object>()
                },
                overrides = Array.Empty<object>()
            },
            options = new
            {
                legend = new
                {
                    displayMode = "list",
                    placement = "bottom",
                    showLegend = true,
                    calcs = Array.Empty<string>()
                },
                tooltip = new { mode = "single", sort = "none" }
            },
            targets = ChannelTargets(channel.NodeId, channel.Channel)
        };
    }

    private static string UnitNote(string rawUnit) =>
        rawUnit.Length == 0 ? " · no unit was declared, so this is plotted as a plain number"
        : UnitRecognised(rawUnit) ? $" · unit {rawUnit}"
        : $" · unit {rawUnit} is not one of Grafana's, so this is plotted as a plain number "
          + "rather than guessed at";

}
