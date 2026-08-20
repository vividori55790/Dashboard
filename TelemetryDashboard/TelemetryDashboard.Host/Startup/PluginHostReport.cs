using System;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// The console block describing the <c>plugins/</c> folder scan.
/// </summary>
/// <remarks>
/// Split out of <see cref="PluginHostSession"/> when extension loading joined it, so neither the
/// session nor its reporting grew past the point where it can be read in one pass.
/// <para>
/// Every outcome of the scan gets a line, including the two that produce nothing: a missing
/// directory and a directory of DLLs that export no plugin are different problems with the same
/// symptom, and an operator who sees neither mentioned assumes the scan simply did not run.
/// </para>
/// </remarks>
internal static class PluginHostReport
{
    /// <summary>Prints what the folder scan found, and why anything was rejected.</summary>
    internal static void PrintDiscovery(PluginDiscovery discovery)
    {
        Console.WriteLine($"  plugins       {discovery.Directory}");
        foreach (string failure in discovery.Failures) Console.Error.WriteLine($"  [plugin-load] {failure}");

        if (!discovery.DirectoryExists)
        {
            Console.WriteLine("                directory not present -- no plugin was loaded.");
            return;
        }

        if (discovery.Plugins.Count == 0)
        {
            Console.WriteLine($"                {discovery.AssembliesScanned} assemblies scanned, no IPlugin found.");
        }
    }

    /// <summary>Prints the services the plugins were handed, and whether each is live.</summary>
    /// <remarks>
    /// A plugin given a router nothing publishes through is a plugin that will never see a packet,
    /// and it has no way to tell. Saying so here is the difference between a quiet plugin and a
    /// plugin quietly attached to nothing.
    /// </remarks>
    internal static void PrintServices(string storePath, bool routerLive, bool serialLive)
    {
        Console.WriteLine($"                store  {storePath}");
        Console.WriteLine($"                router {Describe(routerLive, "ingest attached")}"
            + $", serial {Describe(serialLive, "port open")}");
    }

    private static string Describe(bool live, string why) => live ? $"live -- {why}" : $"idle -- no {why}";
}
