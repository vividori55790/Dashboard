using System;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Recording;
using TelemetryDashboard.Host.Ingest;
using TelemetryDashboard.Host.Outbound;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Stops everything a run started, in the order that keeps what was buffered.
/// </summary>
/// <remarks>
/// The order is the content of this type, and every step of it is load-bearing:
///
/// the pump stops before the plugins, because a plugin's shutdown may still want to write what it
/// was holding and cannot be asked to while frames are arriving; the ingest report is read before
/// the source is disposed, because it reads the serial link's fault counters off the link;
/// the recorder is flushed after the source, because its queue can only be emptied once nothing is
/// still filling it; the outbound relays get a bounded drain of their own, because the last alert
/// before a shutdown is usually the one worth having; and the listener closes last, so a browser
/// watching the stream sees the connection end after the final frame rather than before it.
/// </remarks>
public sealed class ShutdownSequence
{
    public required WebConsoleHost Console { get; init; }
    public required Task Pumping { get; init; }
    public required PluginHostSession Plugins { get; init; }
    public required OutboundRelays Relays { get; init; }
    public ITelemetrySource? Source { get; init; }
    public TelemetryCsvRecorder? Recorder { get; init; }
    public TelemetryIngestPump? Pump { get; init; }

    public async Task DrainAsync(ShutdownCoordinator shutdown)
    {
        System.Console.WriteLine();
        System.Console.WriteLine($"[shutdown] {shutdown.Reason} -- draining.");

        await Pumping.ConfigureAwait(false);

        int active = Plugins.ActivePlugins.Count;
        Plugins.Dispose();
        if (active > 0) System.Console.WriteLine($"[shutdown] {active} plugins shut down.");

        IngestReport.Print(Pump, Source);

        if (Source is not null) await Source.DisposeAsync().ConfigureAwait(false);

        await Relays.DisposeAsync().ConfigureAwait(false);
        foreach (string line in Relays.Summary()) System.Console.WriteLine(line);

        if (Recorder is not null)
        {
            string path = Recorder.StopRecording();
            System.Console.WriteLine($"[shutdown] recording flushed: {Recorder.RecordedPacketCount} rows -> {path}");
        }

        await Console.DisposeAsync().ConfigureAwait(false);
        System.Console.WriteLine("[shutdown] listener closed. Clean exit.");

        shutdown.MarkDrained();
    }
}
