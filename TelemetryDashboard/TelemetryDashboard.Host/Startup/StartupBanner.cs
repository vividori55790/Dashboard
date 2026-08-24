using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Cluster;
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
        PrintIdentity();
        PrintIngest(source);
        PrintRecording(recorder);
        Console.WriteLine();
        Console.WriteLine("  Reachability: the streaming server binds localhost and 127.0.0.1 only.");
        Console.WriteLine("  A browser on another machine needs a tunnel or a reverse proxy in front of it.");
        PrintAccess(console);
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

    /// <summary>
    /// Says who this installation is, so data from it can be told apart from anyone else's.
    /// </summary>
    /// <remarks>
    /// Printed because the failure it guards against is silent. If the directory is not writable
    /// the identity cannot be persisted and a fresh one is generated on every launch, which splits
    /// this node's history into a new series after each restart without ever raising an error. The
    /// only symptom is a chart that starts empty when it should not, so the start-up line says it.
    /// </remarks>
    private static void PrintIdentity()
    {
        NodeIdentity identity = HostNode.Identity;
        Console.WriteLine($"  node          {identity.DisplayName}");

        if (identity.WasCreated)
        {
            Console.WriteLine("                First run for this installation, or the identity file could not");
            Console.WriteLine("                be written. If it recurs every launch, the store below is read-only");
            Console.WriteLine("                and each restart will look like a different machine.");
            Console.WriteLine($"                {NodeIdentityStore.PathFor(AppContext.BaseDirectory)}");
            Console.WriteLine("                It is keyed on this install's path, so moving or renaming the");
            Console.WriteLine($"                directory reads as a new installation. Pin it with {HostNode.AssignedIdVariable}.");
        }
        else if (NodeIdentityStore.HasLegacyFile(AppContext.BaseDirectory))
        {
            // Said once, to the operator who has the file an update would delete. The value has
            // already been copied out; what is left behind is now a stale duplicate, and telling
            // them is cheaper than letting them find two files disagreeing after a hand edit.
            Console.WriteLine($"                Migrated out of {NodeIdentity.FileName} beside the executable, which an");
            Console.WriteLine("                update would have replaced. The identity is unchanged and now lives at");
            Console.WriteLine($"                {NodeIdentityStore.PathFor(AppContext.BaseDirectory)}");
            Console.WriteLine("                The old file is no longer read and can be deleted.");
        }
    }

    /// <summary>Says whether anything has to prove itself before this console answers.</summary>
    /// <remarks>
    /// Both states are worth printing. "Open" is what an operator on a shared machine needs to
    /// know before they walk away from it; "locked" is what the operator who just configured it
    /// needs to see, because the failure mode of a credential is a console that answers 401 to the
    /// tool they forgot to give the password to, and a silent banner leaves them guessing.
    /// </remarks>
    private static void PrintAccess(WebConsoleHost console)
    {
        if (console.Server.Access is null)
        {
            Console.WriteLine("  access        open to this machine -- anything running here can read and command.");
            return;
        }

        Console.WriteLine("  access        a credential is required (HTTP Basic) on every path, including /ws.");
        Console.WriteLine("                Basic is base64, not encryption. On a cleartext link the password is");
        Console.WriteLine("                readable by anything on the path; put TLS in front before it leaves");
        Console.WriteLine("                this machine.");
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

            case PollingTelemetrySource poll:
                Console.WriteLine($"  ingest        polled -- {poll.Description}");
                Console.WriteLine("                A poll samples what the endpoint says at that instant. Anything");
                Console.WriteLine("                shorter than the interval is not observed, and is not claimed to be.");
                break;

            case SseTelemetrySource sse:
                Console.WriteLine($"  ingest        network stream -- {sse.Description}");
                Console.WriteLine("                Measured data from elsewhere: routed, scored and archived");
                Console.WriteLine("                exactly like a device on a port.");
                break;

            // Anything attached that this switch does not recognise still gets described. The
            // previous default reported NONE for every unknown type, so adding a source made the
            // banner announce that nothing was attached while that source was already running --
            // a start-up summary asserting the opposite of what the process was doing.
            case not null:
                Console.WriteLine($"  ingest        {source.Description}");
                break;

            default:
                Console.WriteLine("  ingest        NONE -- no source was supplied.");
                Console.WriteLine("                The timeline stays empty until one is: --serial, --sse or --simulate.");
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
