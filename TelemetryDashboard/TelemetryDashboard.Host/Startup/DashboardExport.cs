using System;
using System.Collections.Generic;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.Host.Configuration;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Writes a standalone HTML console for the profile in force, when one was asked for.
/// </summary>
/// <remarks>
/// Feature 6 in this project's inventory, marked Built since M2. The exporter existed, was tested,
/// and was constructed by nothing — so no running program could produce a dashboard, and the two
/// faults in the page it emitted had never been seen by anybody: a connection chip whose text was
/// the literal string <c>WS CONNECTED</c> with no code to change it, and a widget that, on finding
/// its field absent from a packet, displayed the temperature instead and then zero.
/// <para>
/// The page it writes now connects back to this host and is keyed on the channel names this host
/// actually broadcasts, so opening it on another machine shows the run rather than a placeholder.
/// </para>
/// </remarks>
public static class DashboardExport
{
    /// <summary>Exports the dashboard and prints the outcome. Silent when unconfigured.</summary>
    public static void Print(HostOptions options) => Print(options, new DashboardExporter());

    /// <summary>Overload taking the exporter, so the step can be driven without touching disk twice.</summary>
    public static void Print(HostOptions options, IDashboardExporter exporter)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(exporter);

        if (options.DashboardExportPath is null) return;

        ProfileResolution.Result resolved =
            ProfileResolution.Resolve(options.ProfileId, AppContext.BaseDirectory);

        if (resolved.Warning is not null) Console.Error.WriteLine($"telemetry-host: {resolved.Warning}");

        if (resolved.Error is not null)
        {
            Console.Error.WriteLine($"telemetry-host: {resolved.Error}");
            return;
        }

        MonitoringProfile profile = resolved.Profile ?? MonitoringProfileLibrary.Generic;
        IReadOnlyList<WidgetConfig> widgets = ProfileDashboardWidgets.For(profile);

        try
        {
            string written = exporter.ExportCustomHtmlDashboard(
                options.DashboardExportPath, profile.DisplayName, widgets, options.Port);

            foreach (string line in Render(written, profile, widgets.Count, options.Port))
            {
                Console.WriteLine(line);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // A failed export must not stop the run: the hub is still worth having, and an
            // operator who asked for a file gets told the file is not there rather than finding
            // out by opening a stale one.
            Console.Error.WriteLine(
                $"telemetry-host: could not write dashboard to '{options.DashboardExportPath}': {ex.Message}");
        }
    }

    /// <summary>Builds the report lines, so their wording can be asserted without writing a file.</summary>
    public static string[] Render(string path, MonitoringProfile profile, int widgetCount, int port) =>
    [
        $"[dashboard] wrote {path}",
        $"[dashboard] profile '{profile.Id}' ({profile.DisplayName}): "
        + $"{profile.Channels.Count} channel(s), {widgetCount} card(s)",
        $"[dashboard] open it while this host is running; it connects to ws://localhost:{port}/ws"
    ];
}
