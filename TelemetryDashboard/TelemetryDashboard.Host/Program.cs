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
public static class Program
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
        // Before anything binds a socket. Installing, enabling or removing an extension is an
        // administrative act that ends: a process that also started serving telemetry would leave
        // an operator unable to say whether the install happened before or after this host began
        // running a third party's code.
        if (ExtensionCommandLine.Matches(args)) return ExtensionCommand.Run(args);

        HostOptions options = CommandLineParser.Parse(args, EnvironmentVariables.Read());

        if (options.ShowHelp)
        {
            Console.Out.Write(UsageText.Render());
            return 0;
        }

        if (options.Error is not null)
        {
            Console.Error.WriteLine($"telemetry-host: {options.Error}");
            Console.Error.WriteLine("Run with --help for the accepted arguments.");
            return ExitUsage;
        }

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
        if (options.SerialPort is not null && source is null)
        {
            await console.DisposeAsync().ConfigureAwait(false);
            return ExitSerialFailed;
        }

        TelemetryCsvRecorder? recorder = IngestSetup.StartRecording(options);
        await StartupBanner.PrintAsync(console, source, recorder, shutdown.Token).ConfigureAwait(false);
        await ExtensionCatalogueReport.PrintAsync(options, shutdown.Token).ConfigureAwait(false);
        await UpdateCheck.PrintAsync(options, HostVersion, shutdown.Token).ConfigureAwait(false);

        // The pump is built before the plugins so they are handed the router it is publishing
        // through, and started after them so no frame is routed past a plugin that is not up yet.
        Core.Ingest.JsonChannelMap? channelMap;
        try
        {
            channelMap = IngestSetup.LoadChannelMap(options);
        }
        catch (Exception ex) when (ex is System.IO.FileNotFoundException or System.IO.InvalidDataException)
        {
            Console.Error.WriteLine($"telemetry-host: {ex.Message}");
            await console.DisposeAsync().ConfigureAwait(false);
            return ExitUsage;
        }

        TelemetryIngestPump? pump = source is null
            ? null
            : new TelemetryIngestPump(console.Server, source, recorder, jsonMap: channelMap);
        using PluginHostSession plugins = PluginHostSession.Start(
            options, pump?.Router, (source as SerialTelemetrySource)?.SerialManager);

        // After the pump exists, so a relay is subscribed to the stream that is actually running
        // rather than reporting itself armed over nothing.
        await using OutboundRelays relays = await OutboundRelays.StartAsync(options, pump).ConfigureAwait(false);
        foreach (string line in relays.BannerLines) Console.WriteLine(line);

        StartupBanner.PrintFooter();

        var run = new ShutdownSequence
        {
            Console = console,
            Pumping = pump?.RunAsync(shutdown.Token) ?? Task.CompletedTask,
            Plugins = plugins,
            Relays = relays,
            Source = source,
            Recorder = recorder,
            Pump = pump
        };

        await shutdown.WaitAsync().ConfigureAwait(false);
        await run.DrainAsync(shutdown).ConfigureAwait(false);
        return 0;
    }

    /// <summary>Version reported to the update check, read from the assembly rather than a constant.</summary>
    private static string HostVersion =>
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
