using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Recording;
using TelemetryDashboard.Host.Ingest;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// The block printed once the host is up.
/// </summary>
/// <remarks>
/// Every line is read back from something that already happened — the port the listener bound, the
/// endpoint list the server itself advertises, whether the serial port opened — rather than echoed
/// from the configuration. A banner that reprints what was requested is exactly the surface where
/// a failed serial open reads like a working one.
/// </remarks>
public static class StartupBanner
{
    /// <summary>Prints the banner.</summary>
    public static async Task PrintAsync(
        WebConsoleHost console,
        ITelemetrySource? source,
        TelemetryCsvRecorder? recorder,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> endpoints = await console.QueryAdvertisedEndpointsAsync(cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("TelemetryDashboard -- headless host");
        Console.WriteLine("==================================");
        Console.WriteLine($"  runtime       {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"  rid           {Describe(RuntimeInformation.RuntimeIdentifier)}");
        Console.WriteLine($"  os            {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        Console.WriteLine($"  process arch  {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"  pid           {Environment.ProcessId}");
        Console.WriteLine();
        Console.WriteLine($"  listening     {console.BaseAddress}   (port {console.BoundPort})");
        PrintEndpoints(console, endpoints);
        Console.WriteLine();
        PrintContent(console);
        Console.WriteLine();
        PrintIngest(source);
        PrintRecording(recorder);
        Console.WriteLine();
        Console.WriteLine("  Reachability: the streaming server binds localhost and 127.0.0.1 only.");
        Console.WriteLine("  A browser on another machine needs a tunnel or a reverse proxy in front of it.");
        Console.WriteLine();
    }

    /// <summary>Prints the closing line, once every start-up step has reported.</summary>
    /// <remarks>
    /// Split from <see cref="PrintAsync"/> so "Ctrl-C to stop." is genuinely the last thing an
    /// operator reads. The extension catalogue and the plugin host report after the banner, and a
    /// stop instruction printed before them implied start-up had finished when it had not.
    /// </remarks>
    public static void PrintFooter()
    {
        Console.WriteLine("  Ctrl-C to stop.");
        Console.WriteLine();
    }

    private static void PrintEndpoints(WebConsoleHost console, IReadOnlyList<string> endpoints)
    {
        if (endpoints.Count == 0)
        {
            // Not a formatting fallback: the status endpoint did not answer, and claiming the
            // usual five would be describing a server nobody just reached.
            Console.WriteLine("  endpoints     unavailable -- /api/status did not answer");
            return;
        }

        Console.WriteLine("  endpoints     (as advertised by /api/status)");
        foreach (string endpoint in endpoints)
        {
            Console.WriteLine($"                {console.BaseAddress}{endpoint}");
        }
    }

    private static void PrintContent(WebConsoleHost console)
    {
        Console.WriteLine("  web roots");
        foreach (string root in console.ContentRoots)
        {
            Console.WriteLine($"                {root}");
        }

        Console.WriteLine(console.ClientFile is null
            ? "  console page  none found -- / serves the server's built-in placeholder"
            : $"  console page  {console.ClientFile}");
    }

    private static void PrintIngest(ITelemetrySource? source)
    {
        switch (source)
        {
            case SerialTelemetrySource serial:
                Console.WriteLine($"  ingest        serial, open -- {serial.Description}");
                break;

            case SimulatedTelemetrySource simulated:
                Console.WriteLine($"  ingest        SIMULATED -- {simulated.Description}");
                Console.WriteLine("                every frame carries simulated=true and a 'SIM:' node prefix");
                break;

            default:
                Console.WriteLine("  ingest        NONE -- no serial port was supplied.");
                Console.WriteLine("                The timeline stays empty until one is: --serial <port>.");
                Console.WriteLine("                No synthetic data is substituted; an empty stream is the");
                Console.WriteLine("                truthful state of a hub with nothing attached.");
                break;
        }
    }

    private static void PrintRecording(TelemetryCsvRecorder? recorder)
    {
        Console.WriteLine(recorder is { IsRecording: true }
            ? $"  recording     {recorder.CurrentFilePath}"
            : "  recording     off");
    }

    /// <summary>Runtime identifiers can be empty on a framework-dependent build.</summary>
    private static string Describe(string? runtimeIdentifier) =>
        string.IsNullOrWhiteSpace(runtimeIdentifier) ? "(not set for this build)" : runtimeIdentifier;
}
