using System;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Recording;
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

        return options.Simulate ? new SimulatedTelemetrySource() : null;
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
