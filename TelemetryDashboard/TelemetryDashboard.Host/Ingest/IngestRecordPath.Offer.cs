using System;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Records;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// What the pump hands in: a sample that parsed, and a line that did not.
/// </summary>
/// <remarks>
/// Split from the pipeline construction because this is the edge. Everything here is reached with
/// input somebody else produced, which is where the checks about trusting it belong.
/// </remarks>
public sealed partial class IngestRecordPath
{
    /// <summary>Offers a parsed sample. Returns how many stages accepted it.</summary>
    /// <remarks>
    /// <see cref="DataRecord.Source"/> is overwritten with the port because that is what the field
    /// means — who reported the observation — and the node id the projection puts there by default
    /// is already carried losslessly as <see cref="DataKey.Stream"/>. Nothing is lost and the
    /// transport becomes recoverable downstream.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A packet claims to be synthetic on a run whose source knows it is not. The reason has
    /// changed: the projection used to be unable to carry the flag, and now can, so this is no
    /// longer about losing the mark. It is that a serial port never produces synthetic data and a
    /// simulator never produces anything else, which makes the claim a contradiction -- and
    /// guessing which half is wrong would put fabricated data into an archive of measurements.
    /// <para>
    /// Not raised for a source that has said it cannot know. A network source carries whatever its
    /// peer had, so a frame marked synthetic there is the peer reporting what it generated;
    /// refusing it would drop the data and relabelling it would launder it.
    /// </para>
    /// </exception>
    public ValueTask<int> OfferPacketAsync(
        TelemetryPacket packet, string portName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);

        // The router marks packets from a synthetic source as it produces them, so plugins can see
        // the mark too. That is consistent and expected. What must never happen is a packet
        // claiming to be synthetic on a run reading real hardware -- that would let fabricated data
        // enter an archive of measurements wearing the wrong label.
        if (packet.Flags.HasFlag(PacketFlags.Simulated) && !_isSimulated && !_samplesDecideOrigin)
        {
            throw new InvalidOperationException(
                "A packet claims to be simulated on a run whose source is real hardware. Refusing "
                + "rather than relabelling it: one of the two is wrong, and guessing which would "
                + "put fabricated data into an archive of measurements.");
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
