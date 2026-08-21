using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Analytics.Detectors;
using TelemetryDashboard.Core.Cluster;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Recording;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Streaming;
using TelemetryDashboard.Host.Configuration;
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
    private readonly DetectorPanel _detectors;
    private readonly string _origin;
    private readonly bool _isSimulated;

    private long _published;

    /// <param name="detectors">
    /// Extra detectors to run beside the built-in engine, or null for the configured ones.
    /// </param>
    public IngestPublisher(
        TelemetryStreamingServer server,
        string origin,
        bool isSimulated,
        TelemetryCsvRecorder? recorder,
        IngestRateGuard guard,
        DetectorPanel? detectors = null,
        ArchiveSink? archive = null)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _origin = origin ?? string.Empty;
        _isSimulated = isSimulated;
        _recorder = recorder;
        _detectors = detectors ?? AnalyticsSetup.Shared.Panel;
        Guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _archive = archive;
        _limits = server.Limits;
    }

    /// <summary>Engineering limits in force, or null when the host declared none.</summary>
    private readonly LimitMonitor? _limits;

    /// <summary>
    /// Says a limit was crossed, once per crossing rather than once per sample.
    /// </summary>
    /// <remarks>
    /// A line per sample during an excursion is how an alarm gets muted: a converter held above its
    /// ceiling for a minute at 9 Hz is five hundred identical lines, and the one that mattered is
    /// the first. The count is available at <c>/api/limits</c> for anyone who wants to know how
    /// long it lasted.
    /// <para>
    /// A unit mismatch is said once too, and it is the more urgent of the two: the rule is not
    /// firing at all, and a silent alarm has no other symptom.
    /// </para>
    /// </remarks>
    private void AnnounceLimit(
        string channel, ChannelLimit rule, LimitTransition transition, double value, string? unit)
    {
        switch (transition)
        {
            case LimitTransition.Entered:
                Console.WriteLine($"[limit] {channel}: {rule.Explain(value)}  ({rule.Declaration})");
                break;

            case LimitTransition.Cleared:
                Console.WriteLine($"[limit] {channel}: back inside {rule.Declaration}");
                break;

            case LimitTransition.UnitMismatch when _unitWarned.Add(channel + "|" + rule.Declaration):
                Console.Error.WriteLine(
                    $"[limit] {channel} is NOT being checked against '{rule.Declaration}': " +
                    $"the limit is written in {rule.Unit} and the channel reports " +
                    $"{(string.IsNullOrWhiteSpace(unit) ? "no unit" : unit)}.");
                break;
        }
    }

    private readonly System.Collections.Generic.HashSet<string> _unitWarned = new(StringComparer.Ordinal);

    /// <summary>The rate guard protecting the stream, the console and the recorder.</summary>
    public IngestRateGuard Guard { get; }

    /// <summary>
    /// The configured detectors, and what each of them has actually judged.
    /// </summary>
    /// <remarks>
    /// The built-in rolling z-score above is not the only opinion available any more, and this is
    /// where the others are. Their verdicts are deliberately kept apart rather than merged into the
    /// one score on the wire: a robust detector flagging a spike that the z-score's own poisoned
    /// baseline hid is a diagnosis, and combining the two into a single number destroys it. The
    /// per-detector tallies are the answer to "why is this detector silent" — one offered forty
    /// thousand samples and judging none looks, from the chart, exactly like one that judged them
    /// all and found nothing wrong.
    /// </remarks>
    public DetectorPanel Detectors => _detectors;

    /// <summary>
    /// Which reporting nodes have been heard from, and which have gone quiet.
    /// </summary>
    /// <remarks>
    /// Applied to devices here, because on one machine that is where the failure already exists: a
    /// sensor that stops sending produces no data, and no data renders as a calm channel rather
    /// than as a fault. Counting the samples that arrived can never reveal it — only tracking who
    /// was expected can. The same ledger answers the machine-level question unchanged once
    /// instances exchange data with each other.
    /// </remarks>
    public CoverageLedger Coverage { get; } = new();

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

    /// <summary>The durable archive, when one was asked for.</summary>
    private readonly ArchiveSink? _archive;

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

        Coverage.RecordSample(node);

        // Before the analytics, and independent of them. An engineering limit is a fact about the
        // reading; a z-score is a judgement about the channel's recent history, and a bus that
        // settles at 460 V and stays there becomes normal to the second within a minute. Merging
        // them would let the detector's baseline decide whether a datasheet limit had been passed.
        IReadOnlyList<(ChannelLimit Rule, LimitTransition Transition)> limits =
            _limits?.Evaluate(channel, packet.Value, packet.Unit, packet.Timestamp)
            ?? Array.Empty<(ChannelLimit, LimitTransition)>();

        bool breached = false;
        foreach ((ChannelLimit rule, LimitTransition transition) in limits)
        {
            if (transition is LimitTransition.Entered or LimitTransition.Sustained) breached = true;
            AnnounceLimit(channel, rule, transition, packet.Value, packet.Unit);
        }

        if (breached) packet.Flags |= PacketFlags.AlarmExceeded;

        AnomalyResult analysis = _analytics.AnalyzeChannel(channel, packet.Value);

        // Every other configured detector sees the same sample. None of them may block: the remote
        // model detector answers from the last score that came back rather than waiting for the
        // next one, and reports no verdict at all when there is no fresh answer to report. What
        // they flagged is readable afterwards through Detectors.RecentFlags.
        _detectors.Evaluate(channel, packet.Value, packet.Timestamp);

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
            analysis.HasVerdict ? "OK" : "UNSCORED",
            analysis.ForecastHorizonSec);

        // Archived under the marked node, so the store agrees with the wire and with the CSV about
        // what a channel is called. The score is not archived: a verdict stored beside the value it
        // came from is a second copy that disagrees with the detector after any change to it.
        _archive?.Offer(new TelemetryPacket(node, packet.Variable, packet.Value, packet.Unit, packet.Timestamp));

        Interlocked.Increment(ref _published);

        if (SampleScored is { } handlers)
        {
            handlers(this, new ScoredSample(
                channel, node, packet.Variable, packet.Value, packet.Unit, packet.Timestamp,
                analysis.HasVerdict ? analysis.ZScore : null,
                analysis.HasVerdict ? analysis.IsAnomaly : null,
                analysis.AnalyzerId,
                _isSimulated,
                // Every outcome, recovery included. A relay that only ever hears about breaches
                // can tell an operator a converter left its band and never that it came back.
                limits.Count == 0
                    ? null
                    : limits.Where(l => l.Transition != LimitTransition.None)
                            .Select(l => new BreachedLimit(l.Rule, l.Transition))
                            .ToList()));
        }

        return ValueTask.CompletedTask;
    }
}
