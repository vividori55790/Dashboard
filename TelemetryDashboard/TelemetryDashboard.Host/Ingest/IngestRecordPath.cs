using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Records;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// The record layer of ingest: every line that arrives becomes an accounted-for record.
/// </summary>
/// <remarks>
/// M6 built a universal record path and then nothing routed anything through it, so the layer that
/// was supposed to carry non-numeric data carried nothing at all. This is where it earns its place,
/// and the case that justifies it was already a defect: a line the router did not match and the
/// positional parser rejected used to be dropped with no counter and no message. Numeric samples
/// and unreadable lines now enter the same pipeline, which is what makes "4,000 lines arrived and
/// nothing understood them" a number the operator can read rather than an empty chart.
///
/// The projection round trip is lossy for <see cref="PacketFlags"/> other than
/// <see cref="PacketFlags.IsDerived"/>, because <see cref="DataRecord"/> has no flags field by
/// design. That matters for exactly one flag: a packet that lost <see cref="PacketFlags.Simulated"/>
/// on the way through would arrive downstream indistinguishable from a measurement. So the mark is
/// applied here, after the projection, from the source's own declaration — and a caller offering an
/// already-marked packet is refused rather than silently laundered.
/// </remarks>
public sealed partial class IngestRecordPath
{
    /// <summary>Key used for lines nothing could parse; the stream is the port they arrived on.</summary>
    public const string UnparsedKey = "unparsed";

    private readonly RecordPipeline _pipeline = new();
    private readonly UnrecognisedLineStage _unrecognised = new();
    private readonly NumericPacketStage _numeric;
    private readonly bool _isSimulated;
    private readonly bool _samplesDecideOrigin;

    /// <param name="publish">Receives every numeric record as a packet plus the port it arrived on.</param>
    /// <param name="isSimulated">Whether the source is synthetic; stamped onto each packet here.</param>
    /// <param name="driftWindowSeconds">
    /// Long memory, in seconds, for a per-channel <c>.drift</c> channel; 0 for none. See
    /// <see cref="ChannelDriftProjection"/> for the fault this is the only detector here that sees.
    /// </param>
    /// <param name="watchIntervals">
    /// Whether to derive a <c>.interval</c> channel per channel. See
    /// <see cref="ChannelIntervalProjection"/> for why a dead sensor is otherwise indistinguishable
    /// from a steady one, and why this costs enough to be asked for.
    /// </param>
    public IngestRecordPath(
        Func<TelemetryPacket, string, CancellationToken, ValueTask> publish,
        bool isSimulated,
        bool watchIntervals = false,
        int driftWindowSeconds = 0,
        bool samplesCarryTheirOwnOrigin = false)
    {
        ArgumentNullException.ThrowIfNull(publish);

        _isSimulated = isSimulated;
        _samplesDecideOrigin = samplesCarryTheirOwnOrigin;
        _numeric = new NumericPacketStage("telemetry", (packet, record, token) =>
        {
            if (_isSimulated) packet.Flags |= PacketFlags.Simulated;
            return publish(packet, record.Source, token);
        });

        _pipeline.Register(_numeric).Register(_unrecognised);

        // Registered last on purpose. Its derived record re-enters the pipeline from inside its own
        // ProcessAsync, so with the projection first a channel's interval would be published ahead
        // of the reading it was measured from -- and anything downstream ordering by arrival would
        // see the derivative announce a sample that had not been sent yet.
        if (watchIntervals)
        {
            Intervals = new ChannelIntervalProjection();
            _pipeline.Register(Intervals.Stage(async (record, token) =>
                await _pipeline.DispatchAsync(record, token).ConfigureAwait(false)));
        }

        if (driftWindowSeconds > 0)
        {
            Drift = new ChannelDriftProjection(driftWindowSeconds);
            _pipeline.Register(Drift.Stage(async (record, token) =>
                await _pipeline.DispatchAsync(record, token).ConfigureAwait(false)));
        }
    }


    /// <summary>What each stage has seen, for the shutdown report and the diagnostics surface.</summary>
    public IReadOnlyList<StageActivity> Activity() => _pipeline.Activity();

    /// <summary>The lines nothing could read.</summary>
    public UnrecognisedLineStage Unrecognised => _unrecognised;

    /// <summary>Numeric records whose reading was absent or non-finite, and so not forwarded.</summary>
    public long UnreadableSamples => _numeric.UnreadableCount;
}
