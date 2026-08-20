using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Infrastructure.Plugins;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// The start-up block naming every installed extension and what it is actually doing.
/// </summary>
/// <remarks>
/// Rendering is separated from printing so the wording can be asserted in a test, the same
/// arrangement <see cref="ExtensionCatalogueReport"/> uses and for the same reason: the failure
/// wording is the load-bearing part. An extension that did not load must appear in this block with
/// its reason attached, because a host that lists only what worked is indistinguishable from one
/// with nothing installed.
/// <para>
/// Counts are printed even when they are zero. "0 failed" on every ordinary start is what gives
/// "1 failed" its meaning the day it appears.
/// </para>
/// </remarks>
public static class ExtensionStartupReport
{
    /// <summary>Renders the block for one loader's results.</summary>
    public static IReadOnlyList<string> RenderLines(ExtensionLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);

        var lines = new List<string> { $"  extensions    {loader.Store.Directory}" };

        if (loader.Store.StateFailure is not null)
        {
            lines.Add($"                STATE UNREADABLE -- {loader.Store.StateFailure}");
            lines.Add("                No extension was loaded, and none has been ruled out. The");
            lines.Add("                file was not replaced: your enable/disable choices are intact.");
            return lines;
        }

        IReadOnlyList<InstalledExtension> installed = loader.Store.Extensions;
        if (installed.Count == 0)
        {
            lines.Add("                none installed -- 'extensions install <path>' adds one.");
            return lines;
        }

        var reasons = loader.Skipped.ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase);
        int failed = installed.Count(e => e.LoadFailure is not null);
        int disabled = installed.Count(e => !e.Enabled);

        lines.Add($"                {installed.Count} installed -- {installed.Count - failed - disabled} loaded, "
            + $"{disabled} disabled, {failed} failed");

        foreach (InstalledExtension extension in installed)
        {
            lines.Add($"                {extension.Id,-24}{extension.Name,-30}{extension.Version,-8}{extension.State}");

            if (reasons.TryGetValue(extension.Id, out string? reason) && extension.Enabled)
            {
                lines.Add($"                {string.Empty,-24}{reason}");
            }
        }

        lines.Add($"                {loader.Plugins.Count} plugin type(s) came from installed extensions.");
        return lines;
    }

    /// <summary>Prints the block, sending failure lines to stderr where the host puts its own.</summary>
    /// <remarks>
    /// An operator piping stdout into a log still sees a failed extension. Reporting it only on
    /// stdout would let the one line that matters be the one that scrolls away.
    /// </remarks>
    public static void Print(ExtensionLoader loader)
    {
        foreach (string line in RenderLines(loader)) Console.WriteLine(line);

        foreach (KeyValuePair<string, string> skip in loader.Skipped)
        {
            InstalledExtension? extension = loader.Store.Find(skip.Key);
            if (extension is null || !extension.Enabled) continue;

            Console.Error.WriteLine($"  [extension] {skip.Key} did not load: {skip.Value}");
        }
    }
}
