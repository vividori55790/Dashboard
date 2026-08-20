using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Infrastructure.Plugins;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// What one fetch of the extension catalogue found, and the lines describing it.
/// </summary>
/// <remarks>
/// <see cref="ManifestIndexMarketplace"/> was implemented and constructed by nothing, so the
/// catalogue could not be listed from anywhere. This is its entry point: <c>--extensions</c> names
/// an index, the host reads it once at start-up and prints what it holds.
/// <para>
/// Listing only. Installing runs a third party's code inside this process, so it lives behind an
/// explicit <see cref="ExtensionCommand"/> verb and is never a side effect of naming a catalogue.
/// </para>
/// <para>
/// Rendering is separated from printing so the wording can be asserted. The failure wording is the
/// load-bearing part: a catalogue that could not be reached must never render as an empty one,
/// because "nothing is published" and "I could not read it" lead to opposite conclusions.
/// </para>
/// </remarks>
public sealed class ExtensionCatalogueReport
{
    private ExtensionCatalogueReport(
        string location, IReadOnlyList<ExtensionDescriptor> extensions, int rejectedCount, string? failure)
    {
        Location = location;
        Extensions = extensions;
        RejectedCount = rejectedCount;
        Failure = failure;
    }

    /// <summary>The catalogue index that was consulted.</summary>
    public string Location { get; }

    /// <summary>Entries that parsed. Empty when <see cref="Failure"/> is set.</summary>
    public IReadOnlyList<ExtensionDescriptor> Extensions { get; }

    /// <summary>Entries the index held that could not be parsed, and so are absent above.</summary>
    public int RejectedCount { get; }

    /// <summary>Why the catalogue could not be read, or null when it was.</summary>
    public bool Reachable => Failure is null;

    /// <summary>Transport or format failure, or null on success.</summary>
    public string? Failure { get; }

    /// <summary>Reads the catalogue named by <paramref name="location"/>, never throwing.</summary>
    /// <remarks>
    /// Failures are captured rather than propagated because an unreachable catalogue is an ordinary
    /// state for a hub on an isolated network, and it must not stop a host whose real job is
    /// serving telemetry.
    /// </remarks>
    public static async Task<ExtensionCatalogueReport> FetchAsync(string location, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var marketplace = new ManifestIndexMarketplace(location, http);

        try
        {
            List<ExtensionDescriptor> found = await marketplace
                .FetchAvailableExtensionsAsync(cancellationToken).ConfigureAwait(false);

            return new ExtensionCatalogueReport(location, found, marketplace.LastRejectedCount, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ExtensionCatalogueReport(
                location, Array.Empty<ExtensionDescriptor>(), 0, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Fetches and prints the catalogue block, or does nothing when none was configured.</summary>
    public static async Task PrintAsync(HostOptions options, CancellationToken cancellationToken)
    {
        if (options.ExtensionCatalogue is null) return;

        try
        {
            ExtensionCatalogueReport report =
                await FetchAsync(options.ExtensionCatalogue, cancellationToken).ConfigureAwait(false);

            foreach (string line in report.RenderLines())
            {
                Console.WriteLine(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl-C arrived mid-fetch. Nothing is known about the catalogue either way, and saying
            // that beats a stack trace out of the entry point on an ordinary shutdown.
            Console.WriteLine($"  extensions    fetch abandoned -- shutdown was requested before");
            Console.WriteLine($"                {options.ExtensionCatalogue} answered.");
        }

        Console.WriteLine();
    }

    /// <summary>Renders the block in the startup banner's two-column shape.</summary>
    public IReadOnlyList<string> RenderLines()
    {
        var lines = new List<string>();

        if (!Reachable)
        {
            lines.Add($"  extensions    UNREACHABLE -- {Location}");
            lines.Add($"                {Failure}");
            lines.Add("                Nothing was listed because nothing was read. This is not an");
            lines.Add("                empty catalogue, and no extension has been ruled out.");
            return lines;
        }

        lines.Add($"  extensions    {Location}");

        // The rejected count is printed even when it is zero: "0 rejected" every time is what makes
        // "1 rejected" mean something the day it appears.
        lines.Add($"                {Extensions.Count} listed, {RejectedCount} rejected as unparseable (not listed below)");

        foreach (ExtensionDescriptor extension in Extensions)
        {
            lines.Add($"                {extension.Id,-24}{extension.Name,-32}{extension.Version}");
        }

        // An index that is not a manifest array at all yields no entries and no rejections, exactly
        // like an index that is genuinely empty. The host cannot tell them apart, so it says so
        // rather than reporting the more flattering of the two.
        if (Extensions.Count == 0 && RejectedCount == 0)
        {
            lines.Add("                An empty catalogue and a document that is not a manifest array");
            lines.Add("                look identical here -- check the index if you expected entries.");
        }

        lines.Add("                Listed only -- nothing was installed.");

        // The next step, named, rather than left for the operator to find. A catalogue that can be
        // read but not acted on was the previous state of this feature.
        lines.Add(Location.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? "                An http(s) catalogue can be listed but not installed from."
            : $"                To install: {ExtensionCommandLine.Verb} install --catalogue {Location} <id>");

        return lines;
    }
}
