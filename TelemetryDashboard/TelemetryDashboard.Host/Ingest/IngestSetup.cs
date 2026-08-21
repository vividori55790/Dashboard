using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Ingest;
using TelemetryDashboard.Core.Recording;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.Host.Configuration;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// Builds the ingest side of a run from the configuration: which source, and whether to record.
/// </summary>
/// <remarks>
/// Separate from the entry point so the decision "what feeds this host" is one readable place, and
/// so the lifecycle code that starts and drains it does not also have to carry the rules about
/// what counts as a source in the first place.
/// </remarks>
public static class IngestSetup
{
    /// <summary>
    /// Opens the configured source, or returns null when none was configured or the port failed.
    /// </summary>
    /// <remarks>
    /// A serial port that was asked for and did not open is reported and left closed, so the caller
    /// can abort the start rather than serve an empty stream. Both states look identical from a
    /// browser — no frames arriving — and only one of them means "your device is not plugged in".
    /// Simulation is reached only through an explicit flag; it is never a fallback for this.
    /// </remarks>
    public static async Task<ITelemetrySource?> OpenSourceAsync(HostOptions options, CancellationToken cancellationToken)
    {
        if (options.PollEndpoint is not null)
        {
            return new PollingTelemetrySource(options.PollEndpoint, options.PollInterval);
        }

        if (options.SseEndpoint is not null)
        {
            return new SseTelemetrySource(options.SseEndpoint);
        }

        if (options.ReplayPath is not null)
        {
            var replay = new ReplayTelemetrySource(options.ReplayPath, options.ReplaySpeed);

            try
            {
                if (replay.Load()) return replay;

                Console.Error.WriteLine(
                    $"telemetry-host: '{options.ReplayPath}' holds no playable rows. A recording is "
                    + "the recorder's CSV layout; an empty or header-only file has nothing to play.");
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"telemetry-host: could not read '{options.ReplayPath}': {ex.Message}");
            }

            return null;
        }

        if (options.SerialPort is not null)
        {
            var serial = new SerialTelemetrySource(options.SerialPort, options.BaudRate);
            if (await serial.OpenAsync(cancellationToken).ConfigureAwait(false)) return serial;

            await serial.DisposeAsync().ConfigureAwait(false);
            Console.Error.WriteLine(
                $"telemetry-host: could not open serial port '{options.SerialPort}' at {options.BaudRate} baud. " +
                "Check that the device is attached and not held by another process.");
            return null;
        }

        if (!options.Simulate) return null;

        // Resolved through the same helper the dashboard export uses, so a run cannot generate one
        // machine's channels while its exported page describes another.
        ProfileResolution.Result resolved =
            ProfileResolution.Resolve(options.ProfileId, AppContext.BaseDirectory);

        if (resolved.Warning is not null) Console.Error.WriteLine($"telemetry-host: {resolved.Warning}");

        if (resolved.Error is not null)
        {
            Console.Error.WriteLine($"telemetry-host: {resolved.Error}");
            return null;
        }

        return new SimulatedTelemetrySource(resolved.Profile);
    }

    /// <summary>
    /// Loads the channel map, or returns null when none was configured.
    /// </summary>
    /// <remarks>
    /// A map that cannot be read stops the start. The alternative is a host that connects to the
    /// feed, reads every event and charts nothing, which looks exactly like a feed that has gone
    /// quiet — and sends the operator looking at the wrong end of the problem.
    /// </remarks>
    public static JsonChannelMap? LoadChannelMap(HostOptions options)
    {
        if (options.ChannelMapPath is null) return null;
        return JsonChannelMapReader.Load(options.ChannelMapPath);
    }

    /// <summary>Starts a CSV recording when one was asked for, otherwise returns null.</summary>
    public static TelemetryCsvRecorder? StartRecording(HostOptions options)
    {
        if (options.RecordingDirectory is null) return null;

        var recorder = new TelemetryCsvRecorder();
        recorder.StartRecording(options.RecordingDirectory);
        return recorder;
    }
}
