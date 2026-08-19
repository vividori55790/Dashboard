using System;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Recording;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Streaming;
using TelemetryDashboard.Host.Outbound;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// Turns one parsed sample into a scored frame on the wire and a row in the recording.
/// </summary>
/// <remarks>
/// Separated from the pump because they fail differently and change for different reasons: the
/// pump is about keeping a source alive and losing nothing, this is about what a sample means once
/// it has arrived. It owns the analytics engine for the same reason the pump used to — the rolling
/// baseline is per-run history, and sharing one across two streams would score each against the
/// other's statistics.
/// </remarks>
public sealed class IngestPublisher
{
    private readonly TelemetryStreamingServer _server;
    private readonly TelemetryCsvRecorder? _recorder;
    private readonly TelemetryMlAnalyticsEngine _analytics = new();
    private readonly string _origin;
    private readonly bool _isSimulated;

    private long _published;

    public IngestPublisher(
        TelemetryStreamingServer server,
        string origin,
        bool isSimulated,
        TelemetryCsvRecorder? recorder,
        IngestRateGuard guard)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _origin = origin ?? string.Empty;
        _isSimulated = isSimulated;
        _recorder = recorder;
        Guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    /// <summary>The rate guard protecting the stream, the console and the recorder.</summary>
    public IngestRateGuard Guard { get; }

    /// <summary>
    /// Raised for every sample that reaches the wire, carrying the verdict as it was actually
    /// reached — <c>null</c> during warm-up rather than a confident zero.
    /// </summary>
    /// <remarks>
    /// Subscribers must not block: this fires on the ingest path, so a relay that waits on a
    /// network stalls the console and the recording with it. The relays in
    /// <see cref="TelemetryDashboard.Host.Outbound"/> hand off to a bounded queue for that reason.
    /// </remarks>
    public event EventHandler<ScoredSample>? SampleScored;

    /// <summary>Samples that reached the wire.</summary>
    public long SamplesPublished => Interlocked.Read(ref _published);

    /// <summary>Scores one sample and publishes it, unless the guard has isolated its channel.</summary>
    /// <remarks>
    /// The guard runs before the analytics engine on purpose: a sample that is going to be dropped
    /// must not enter the rolling baseline either, or the statistics would describe a stream that
    /// was never served.
    /// </remarks>
    public ValueTask PublishAsync(TelemetryPacket packet, string portName, CancellationToken cancellationToken)
    {
        string node = TelemetryFrame.MarkNode(packet.NodeId, _isSimulated);
        string channel = $"{node}.{packet.Variable}";

        if (!Guard.Allow(channel)) return ValueTask.CompletedTask;

        AnomalyResult analysis = _analytics.AnalyzeChannel(channel, packet.Value);

        _server.BroadcastTelemetry(
            TelemetryFrame.Create(packet, analysis, _origin, _isSimulated, portName));

        // The CSV schema has no column for "no verdict yet", so the status column carries it. A
        // warm-up sample would otherwise be stored as a confident 0.00 sigma alongside real ones.
        _recorder?.RecordSample(
            node,
            packet.Variable,
            packet.Value,
            analysis.ZScore,
            analysis.IsAnomaly,
            analysis.PredictedValueIn60s,
            analysis.HasVerdict ? "OK" : "UNSCORED");

        Interlocked.Increment(ref _published);

        if (SampleScored is { } handlers)
        {
            handlers(this, new ScoredSample(
                channel, node, packet.Variable, packet.Value, packet.Unit, packet.Timestamp,
                analysis.HasVerdict ? analysis.ZScore : null,
                analysis.HasVerdict ? analysis.IsAnomaly : null,
                analysis.AnalyzerId,
                _isSimulated));
        }

        return ValueTask.CompletedTask;
    }
}
