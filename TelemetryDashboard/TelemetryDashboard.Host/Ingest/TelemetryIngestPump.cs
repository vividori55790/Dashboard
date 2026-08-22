using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Ingest;
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
    private readonly JsonChannelMap? _jsonMap;

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

    /// <summary>Which reporting nodes were heard from, and which fell silent during the run.</summary>
    public Core.Cluster.CoverageLedger Coverage => _publisher.Coverage;

    /// <summary>The record layer: per-stage tallies and the lines nothing could parse.</summary>
    public IngestRecordPath Records => _records;

    /// <summary>Publishes the host's declared expressions as channels of their own.</summary>
    public ComputedChannelPump Computed { get; }

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
        int maxChannelRatePerSecond = IngestRateGuard.DefaultMaxChannelRatePerSecond,
        JsonChannelMap? jsonMap = null,
        ArchiveSink? archive = null,
        bool watchIntervals = false)
    {
        _jsonMap = jsonMap;
        _source = source;
        _publisher = new IngestPublisher(
            server, source.Origin, source.IsSimulated, recorder,
            new IngestRateGuard(maxChannelRatePerSecond), detectors: null, archive: archive);
        _records = new IngestRecordPath(_publisher.PublishAsync, source.IsSimulated, watchIntervals);

        // Through the same publisher as a measured sample, which is the whole point: a derived
        // channel that skipped the scoring, the recording or the archive would be a number on a
        // chart that no alert could fire on and no query could find afterwards.
        Computed = new ComputedChannelPump(server, _publisher.PublishAsync);

        // So a plugin, which is delivered to from inside the router, sees the same truth the wire
        // frame carries rather than an unmarked copy.
        _router.SourceIsSimulated = source.IsSimulated;

        foreach (RoutingRule rule in DefaultRoutingRules.Create())
        {
            _router.RegisterRule(rule);
        }
    }

    /// <summary>
    /// Consumes the source until cancelled. Never throws: a source that dies must not take the
    /// server down with it, because the console and the recorded timeline are still worth serving.
    /// </summary>
    /// <summary>
    /// Runs everything this pump does: the source read loop and the silence sweep beside it.
    /// </summary>
    /// <remarks>
    /// One entry point so a caller cannot start half of it. The two are genuinely separate loops --
    /// see <see cref="SweepIntervalsAsync"/> for why the sweep cannot be driven by the reader --
    /// and a host that started only the reader would look correct right up until a link dropped.
    /// </remarks>
    public Task RunAllAsync(CancellationToken cancellationToken) =>
        Task.WhenAll(RunAsync(cancellationToken), SweepIntervalsAsync(cancellationToken));

    /// <summary>
    /// Runs the silence sweep, which has to outlive this pump's own read loop.
    /// </summary>
    /// <remarks>
    /// Started beside <see cref="RunAsync"/> rather than inside it. A source that has stopped
    /// delivering is precisely the condition the sweep watches for, so driving it from the read
    /// loop would silence it at the moment it mattered -- and a replay reaching the end of its file
    /// ends that loop outright.
    /// </remarks>
    public Task SweepIntervalsAsync(CancellationToken cancellationToken) =>
        _records.SweepIntervalsAsync(cancellationToken);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Alongside the read loop rather than inside it. A derived channel is computed for an
        // instant every input can answer, which is not the instant any single arrival carries, so
        // driving it from arrivals would tie it to whichever channel happened to be fastest.
        //
        // Its own token, linked to the caller's, because the read loop can end on its own -- a
        // replayed recording runs out -- and the computed loop is a timer that never does. Started
        // on the caller's token and awaited below, that combination deadlocked: the source
        // finished, the finally waited for a loop nothing had asked to stop, and the whole test
        // suite hung on it.
        using var stopComputed = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task derived = Computed.RunAsync(stopComputed.Token);

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

            // The loop ending without cancellation means the source ran out. A live feed does not
            // do that, but a recording does -- and a stream that simply goes quiet is
            // indistinguishable from a source that died, which is the one thing this project
            // refuses to leave ambiguous.
            if (!cancellationToken.IsCancellationRequested)
            {
                SourceExhausted = true;
                Console.WriteLine(
                    $"[ingest] {_source.Origin} source reached its end after {SamplesPublished:N0} sample(s). "
                    + "The console keeps serving what was read; no more will arrive.");
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
        finally
        {
            // Observed rather than abandoned. An unawaited task that faults is a fault nobody
            // hears about, and this host has already been bitten once by exactly that -- a
            // fire-and-forget dispatch that swallowed the exception and left the caller waiting.
            try
            {
                stopComputed.Cancel();
                await derived.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                FaultMessage ??= $"computed channels stopped: {ex.GetType().Name}: {ex.Message}";
                Console.Error.WriteLine($"[computed] {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Whether the source ended on its own rather than being cancelled.</summary>
    /// <remarks>
    /// True only for a finite source -- a replayed recording. A serial port or a stream that stops
    /// is a fault and is reported through <see cref="FaultMessage"/> instead.
    /// </remarks>
    public bool SourceExhausted { get; private set; }

    /// <summary>The channel map this run is projecting JSON documents through, if any.</summary>
    public JsonChannelMap? JsonMap => _jsonMap;

    /// <summary>
    /// Configured rules first, then the channel map, and positional parsing only as a last resort.
    /// </summary>
    /// <remarks>
    /// Order is by specificity. A routing rule names an exact frame format; a channel map names
    /// exact paths in a document; positional parsing knows nothing and labels columns
    /// <c>field1</c>, <c>field2</c>. Running the guesser before either of the two that were
    /// configured would let it claim a line somebody had already described precisely.
    /// </remarks>
    private List<TelemetryPacket> Resolve(RawPacket raw)
    {
        List<TelemetryPacket> routed = _router.Route(raw).ToList();
        if (routed.Count > 0) return routed;

        if (_jsonMap is not null)
        {
            IReadOnlyList<TelemetryPacket> mapped = _jsonMap.Project(raw.RawLine, raw.TimestampUtc, raw.PortName);
            if (mapped.Count > 0) return Mark(mapped.ToList());
        }

        return Mark(RawPayloadParser.Parse(raw));
    }

    /// <summary>
    /// Stamps the synthetic marker on packets that did not come through the router.
    /// </summary>
    /// <remarks>
    /// The router marks what it produces, but only a line matching a configured rule goes through
    /// it. A JSON document or a bare column of numbers is parsed outside it, and those packets used
    /// to travel unmarked until the publish path caught them — which is too late for anything
    /// reading them earlier. Marking every path at the point of resolution means there is one
    /// answer to "is this measured", not three.
    /// </remarks>
    private List<TelemetryPacket> Mark(List<TelemetryPacket> packets)
    {
        // Through the router's delivery path, so a packet decoded by the channel map or the
        // positional parser reaches plugins, alarm evaluation and the synthetic mark exactly as a
        // rule-matched one does. Before this, a plugin on a JSON feed received nothing while every
        // other surface showed the data arriving.
        foreach (TelemetryPacket packet in packets)
        {
            _router.Deliver(packet);
        }

        return packets;
    }
}
