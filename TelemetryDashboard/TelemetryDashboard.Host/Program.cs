using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Recording;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Host.Ingest;
using TelemetryDashboard.Host.Outbound;
using TelemetryDashboard.Host.Startup;

namespace TelemetryDashboard.Host;

/// <summary>
/// Entry point of the headless telemetry hub.
/// </summary>
/// <remarks>
/// The desktop shell used to be the only way to start the backbone, which pinned the product to
/// Windows even though Core, Infrastructure and Plugins are plain net8.0 and call nothing
/// Windows-only. This host starts the same objects with a console where the window was, so the hub
/// runs wherever .NET 8 runs and an operator reaches it from a phone, a Mac or a Linux box through
/// the browser console it already ships.
/// </remarks>
public static partial class Program
{
    /// <summary>Exit code for a command line that could not be understood.</summary>
    public const int ExitUsage = 64;

    /// <summary>Exit code for a port that could not be bound.</summary>
    public const int ExitListenFailed = 70;

    /// <summary>Exit code for a serial port that was requested and could not be opened.</summary>
    public const int ExitSerialFailed = 71;

    /// <summary>Starts the host and blocks until a shutdown signal arrives.</summary>
    public static async Task<int> Main(string[] args)
    {
        // Subcommands, the command line, and the output encoding -- everything that decides whether
        // there is a run at all. See Program.Startup.cs.
        if (PreFlight(args, out HostOptions options) is { } preFlightExit) return preFlightExit;

        using var shutdown = new ShutdownCoordinator();

        WebConsoleHost console;
        try
        {
            console = WebConsoleHost.Start(options);
        }
        catch (Exception ex) when (ex is HttpListenerException or SocketException)
        {
            Console.Error.WriteLine($"telemetry-host: cannot listen on port {options.Port}: {ex.Message}");
            return ExitListenFailed;
        }

        ITelemetrySource? source = await IngestSetup.OpenSourceAsync(options, shutdown.Token).ConfigureAwait(false);
        if (source is null && options.SourceRequested)
        {
            // IngestSetup has already said what went wrong. What matters here is that the run ends:
            // continuing would serve a console over an empty timeline under an exit code that says
            // the host started normally.
            await console.DisposeAsync().ConfigureAwait(false);
            return options.SerialPort is not null ? ExitSerialFailed : ExitUsage;
        }

        TelemetryCsvRecorder? recorder = IngestSetup.StartRecording(options);
        await StartupBanner.PrintAsync(console, source, recorder, shutdown.Token).ConfigureAwait(false);
        await ExtensionCatalogueReport.PrintAsync(options, shutdown.Token).ConfigureAwait(false);
        await UpdateCheck.PrintAsync(options, HostVersion, shutdown.Token).ConfigureAwait(false);
        DashboardExport.Print(options);

        if (!IngestSetup.TryLoadChannelMap(options, out Core.Ingest.JsonChannelMap? channelMap, out string? mapError))
        {
            return await StartupRefusal.EndAsync(console, mapError!).ConfigureAwait(false);
        }

        if (!ArchiveSetup.TryOpen(options, console.Server, out ArchiveSink? opened, out string? refusal))
        {
            return await StartupRefusal.EndAsync(console, refusal!).ConfigureAwait(false);
        }
        await using ArchiveSink? archive = opened;

        HostFeatureSetup.Attach(options, console.Server, source);

        if (!RoutingSetup.TryResolve(options, HostFeatureSetup.ActiveProfile(options),
                out RoutingSetup.Result routing, out string? ruleRefusal))
        {
            return await StartupRefusal.EndAsync(console, ruleRefusal!).ConfigureAwait(false);
        }

        foreach (string line in routing.Lines) Console.WriteLine(line);

        // After the archive and HostFeatureSetup, before the plugins: nothing is published before
        // there is somewhere to keep it, and no frame is routed past a plugin that is not up yet.
        TelemetryIngestPump? pump = source is null
            ? null
            : new TelemetryIngestPump(console.Server, source, recorder, jsonMap: channelMap, archive: archive,
                watchIntervals: options.WatchIntervals, driftWindowSeconds: options.DriftWindowSeconds,
                rules: routing.Rules);
        // After the pump, which owns the ledger, and before the footer so it lands in the banner.
        foreach (string line in CoverageSetup.Apply(options, pump, console.Server)) Console.WriteLine(line);

        using PluginHostSession plugins = PluginHostSession.Start(
            options, pump?.Router, IngestSetup.SerialManagerOf(source));

        // After the pump, so a relay subscribes to a running stream rather than reporting itself
        // armed over nothing.
        await using OutboundRelays relays = await OutboundRelays.StartAsync(
            options, pump, IngestSetup.SerialManagerOf(source), archive?.Store).ConfigureAwait(false);
        foreach (string line in relays.BannerLines) Console.WriteLine(line);

        StartupBanner.PrintFooter();

        var run = new ShutdownSequence
        {
            Console = console,
            // RunAllAsync, not RunAsync: this pump is two loops now, and which ones is its business.
            Pumping = BackgroundWork.RunAsync(pump, archive, options, shutdown.Token),
            Plugins = plugins,
            Relays = relays,
            Source = source,
            Recorder = recorder,
            Pump = pump,
            Archive = archive,
            Options = options
        };

        await shutdown.WaitAsync().ConfigureAwait(false);
        await run.DrainAsync(shutdown).ConfigureAwait(false);

        return 0;
    }

    /// <summary>Version reported to the update check, read from the assembly rather than a constant.</summary>
    private static string HostVersion =>
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
