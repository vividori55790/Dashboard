using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Ingest;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Host.Ingest;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Listens to whatever is on the bench and writes a rules file describing it.
/// </summary>
/// <remarks>
/// The half that was missing. <c>--rules</c> could rename a device's channels into the profile's
/// terms, and writing that file meant already knowing the device's names, units and frame tag. This
/// finds them out, and it does it through the same router the serving host uses so that what it
/// reports is what a real run will see rather than a second opinion about the same wire.
/// </remarks>
internal static class SniffCommand
{
    public static int Run(string[] args)
    {
        SniffCommandLine command = SniffCommandLine.Parse(args);

        if (command.ShowHelp)
        {
            Console.Out.Write(SniffUsageText.Render());
            return 0;
        }

        if (command.Error is not null)
        {
            Console.Error.WriteLine($"telemetry-host sniff: {command.Error}");
            Console.Error.WriteLine("Run 'sniff --help' for the accepted arguments.");
            return Program.ExitUsage;
        }

        if (!NamesASource(command.Source))
        {
            Console.Error.WriteLine(
                "telemetry-host sniff: nothing to listen to. Name a source, e.g. --serial COM3, "
                + "--sse <url> or --replay <recording.csv>.");
            return Program.ExitUsage;
        }

        // Before opening anything. Refusing after fifteen seconds of listening would waste the one
        // thing this command costs, and refusing at all is the point: a rules file somebody has
        // edited is work, and overwriting it silently is the worst outcome here.
        if (!command.Verify && File.Exists(command.OutputPath) && !command.Force)
        {
            Console.Error.WriteLine(
                $"telemetry-host sniff: '{command.OutputPath}' already exists. Write somewhere else "
                + "with --out <file>, or replace it with --force.");
            return Program.ExitUsage;
        }

        return RunAsync(command).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Whether anything at all was named to listen to.
    /// </summary>
    /// <remarks>
    /// Not <c>HostOptions.SourceRequested</c>, which deliberately excludes the network sources: it
    /// answers "should the host refuse to start if this fails to open", and an SSE address cannot
    /// fail to open, it fails to reach. The question here is the different one of whether the
    /// operator named anything, and an <c>--sse</c> address is naming something.
    /// </remarks>
    private static bool NamesASource(HostOptions options) =>
        options.SerialPort is not null || options.ReplayPath is not null || options.Simulate
        || options.SseEndpoint is not null || options.PollEndpoint is not null;

    private static async Task<int> RunAsync(SniffCommandLine command)
    {
        using var stop = new CancellationTokenSource(command.Duration);

        ITelemetrySource? source =
            await IngestSetup.OpenSourceAsync(command.Source, CancellationToken.None).ConfigureAwait(false);

        if (source is null)
        {
            // OpenSourceAsync has already said what went wrong.
            return command.Source.SerialPort is not null ? Program.ExitSerialFailed : Program.ExitUsage;
        }

        MonitoringProfile? profile = HostFeatureSetup.ActiveProfile(command.Source);

        if (!RoutingSetup.TryResolve(command.Source, profile, out RoutingSetup.Result routing, out string? refusal))
        {
            Console.Error.WriteLine($"telemetry-host sniff: {refusal}");
            return Program.ExitUsage;
        }

        var router = new DataRouter();
        foreach (RoutingRule rule in routing.Rules) router.RegisterRule(rule);

        Console.WriteLine(
            $"Listening to {source.Origin} for {command.Duration.TotalSeconds:0.#}s. "
            + (command.Verify
                ? "Nothing is published, recorded or written."
                : "Nothing is published and nothing is recorded."));

        var survey = new WireSurvey();
        await SniffListener.ListenAsync(source, router, survey, command.Duration, stop.Token).ConfigureAwait(false);

        if (source is IAsyncDisposable disposable) await disposable.DisposeAsync().ConfigureAwait(false);

        SniffReport.Print(survey, profile);

        if (command.Verify)
        {
            foreach (string line in SniffVerification.Render(survey, profile, routing.Rules.Count))
            {
                Console.WriteLine(line);
            }

            return SniffVerification.ExitCode(survey, profile);
        }

        string draft = RuleDraft.Render(survey, profile, "TelemetryDashboard.Host " + command.Invocation);
        return Write(command.OutputPath, draft, survey);
    }

    private static int Write(string path, string draft, WireSurvey survey)
    {
        try
        {
            File.WriteAllText(path, draft, Core.Services.Utf8Files.WithoutBom);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"telemetry-host sniff: could not write '{path}': {ex.Message}");
            return Program.ExitUsage;
        }

        Console.WriteLine($"Wrote {path}. Fill in the entries it left commented out, then start the "
                          + "host with --rules " + path + ".");

        // A run that heard nothing still writes a file, and the file says so. What it must not do
        // is report success in the same words as a run that worked.
        return survey.Lines == 0 ? 1 : 0;
    }
}
