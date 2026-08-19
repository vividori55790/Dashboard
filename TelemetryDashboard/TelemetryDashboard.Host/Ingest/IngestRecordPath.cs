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
public sealed class IngestRecordPath
{
    /// <summary>Key used for lines nothing could parse; the stream is the port they arrived on.</summary>
    public const string UnparsedKey = "unparsed";

    private readonly RecordPipeline _pipeline = new();
    private readonly UnrecognisedLineStage _unrecognised = new();
    private readonly NumericPacketStage _numeric;
    private readonly bool _isSimulated;

    /// <param name="publish">Receives every numeric record as a packet plus the port it arrived on.</param>
    /// <param name="isSimulated">Whether the source is synthetic; stamped onto each packet here.</param>
    public IngestRecordPath(Func<TelemetryPacket, string, CancellationToken, ValueTask> publish, bool isSimulated)
    {
        ArgumentNullException.ThrowIfNull(publish);

        _isSimulated = isSimulated;
        _numeric = new NumericPacketStage("telemetry", (packet, record, token) =>
        {
            if (_isSimulated) packet.Flags |= PacketFlags.Simulated;
            return publish(packet, record.Source, token);
        });

        _pipeline.Register(_numeric).Register(_unrecognised);
    }

    /// <summary>What each stage has seen, for the shutdown report and the diagnostics surface.</summary>
    public IReadOnlyList<StageActivity> Activity() => _pipeline.Activity();

    /// <summary>The lines nothing could read.</summary>
    public UnrecognisedLineStage Unrecognised => _unrecognised;

    /// <summary>Numeric records whose reading was absent or non-finite, and so not forwarded.</summary>
    public long UnreadableSamples => _numeric.UnreadableCount;

    /// <summary>Offers a parsed sample. Returns how many stages accepted it.</summary>
    /// <remarks>
    /// <see cref="DataRecord.Source"/> is overwritten with the port because that is what the field
    /// means — who reported the observation — and the node id the projection puts there by default
    /// is already carried losslessly as <see cref="DataKey.Stream"/>. Nothing is lost and the
    /// transport becomes recoverable downstream.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The packet already claims to be simulated. The projection cannot carry that flag, so
    /// accepting it here would strip the one mark that keeps synthetic data identifiable.
    /// </exception>
    public ValueTask<int> OfferPacketAsync(
        TelemetryPacket packet, string portName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (packet.Flags.HasFlag(PacketFlags.Simulated))
        {
            throw new InvalidOperationException(
                "A packet reaching the record path must not be pre-marked as simulated: the "
                + "projection has no field for the flag and would drop it. Let the path stamp it "
                + "from the source instead.");
        }

        DataRecord record = TelemetryPacketProjection.ToRecord(packet) with { Source = portName ?? string.Empty };
        return _pipeline.DispatchAsync(record, cancellationToken);
    }

    /// <summary>Offers a line nothing could parse, keyed by the port it arrived on.</summary>
    public ValueTask<int> OfferUnparsedAsync(RawPacket raw, CancellationToken cancellationToken = default)
    {
        var record = new DataRecord
        {
            Key = new DataKey(
                string.IsNullOrEmpty(raw.PortName) ? "unknown-port" : raw.PortName,
                UnparsedKey),
            // The field is named TimestampUtc and the readers that fill it use DateTime.UtcNow, so
            // relabelling an unspecified kind is accurate here; converting would shift it.
            Timestamp = new DateTimeOffset(DateTime.SpecifyKind(raw.TimestampUtc, DateTimeKind.Utc)),
            Value = new DataValue.Text(raw.RawLine ?? string.Empty),
            Source = _isSimulated ? "SIMULATION" : "REAL_HARDWARE"
        };

        return _pipeline.DispatchAsync(record, cancellationToken);
    }
}
