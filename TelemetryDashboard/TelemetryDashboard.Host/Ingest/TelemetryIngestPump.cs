using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Recording;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// Drives one telemetry source through parsing, accounting, scoring, broadcast and recording.
/// </summary>
/// <remarks>
/// This is the whole ingest path of the headless host, and it is the same shape the desktop shell
/// runs: route the raw line, fall back to positional parsing, then hand the result to the record
/// path, which is what carries it to the publisher. The pump owns the router rather than receiving
/// it, because its rule set is per-run configuration and a shared instance would let one stream's
/// rules reinterpret another's frames.
///
/// Nothing that arrives is discarded without a counter. A line no rule matched and the positional
/// parser rejected used to fall out of this loop silently; it now reaches the record path as text
/// and is tallied, so "the device is transmitting something we cannot read" stops looking exactly
/// like "the device is not transmitting".
/// </remarks>
public sealed class TelemetryIngestPump
{
    private readonly ITelemetrySource _source;
    private readonly DataRouter _router = new();
    private readonly IngestPublisher _publisher;
    private readonly IngestRecordPath _records;

    /// <summary>Samples broadcast so far.</summary>
    public long SamplesPublished => _publisher.SamplesPublished;

    /// <summary>The router this pump is publishing through.</summary>
    /// <remarks>
    /// Exposed so a plugin can be handed the router that is actually carrying frames rather than a
    /// second instance built for it. A plugin registered on an idle copy would parse nothing,
    /// score nothing and log nothing, while every surface reported it as loaded and running.
    /// </remarks>
    public DataRouter Router => _router;

    /// <summary>The rate guard protecting the stream, the console and the recorder.</summary>
    public IngestRateGuard Guard => _publisher.Guard;

    /// <summary>The record layer: per-stage tallies and the lines nothing could parse.</summary>
    public IngestRecordPath Records => _records;

    /// <summary>Raised for every sample that reaches the wire. See the publisher for the contract.</summary>
    public event EventHandler<Outbound.ScoredSample> SampleScored
    {
        add => _publisher.SampleScored += value;
        remove => _publisher.SampleScored -= value;
    }

    /// <summary>Why the pump stopped early, or null when it ran to cancellation.</summary>
    public string? FaultMessage { get; private set; }

    /// <summary>Wires a source to a running streaming server, optionally recording to disk.</summary>
    /// <param name="maxChannelRatePerSecond">
    /// Per-channel ceiling; zero disables the guard. See <see cref="IngestRateGuard"/> for why
    /// dropping is announced rather than silent.
    /// </param>
    public TelemetryIngestPump(
        TelemetryStreamingServer server,
        ITelemetrySource source,
        TelemetryCsvRecorder? recorder = null,
        int maxChannelRatePerSecond = IngestRateGuard.DefaultMaxChannelRatePerSecond)
    {
        _source = source;
        _publisher = new IngestPublisher(
            server, source.Origin, source.IsSimulated, recorder, new IngestRateGuard(maxChannelRatePerSecond));
        _records = new IngestRecordPath(_publisher.PublishAsync, source.IsSimulated);

        foreach (RoutingRule rule in DefaultRoutingRules.Create())
        {
            _router.RegisterRule(rule);
        }
    }

    /// <summary>
    /// Consumes the source until cancelled. Never throws: a source that dies must not take the
    /// server down with it, because the console and the recorded timeline are still worth serving.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (RawPacket raw in _source.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                List<TelemetryPacket> packets = Resolve(raw);

                if (packets.Count == 0)
                {
                    await _records.OfferUnparsedAsync(raw, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                foreach (TelemetryPacket packet in packets)
                {
                    await _records.OfferPacketAsync(packet, raw.PortName, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested.
        }
        catch (Exception ex)
        {
            FaultMessage = $"{ex.GetType().Name}: {ex.Message}";
            Console.Error.WriteLine($"[ingest] source stopped: {FaultMessage}");
        }
    }

    /// <summary>Configured rules first, positional parsing only when none of them matched.</summary>
    private List<TelemetryPacket> Resolve(RawPacket raw)
    {
        List<TelemetryPacket> routed = _router.Route(raw).ToList();
        return routed.Count > 0 ? routed : RawPayloadParser.Parse(raw);
    }
}
