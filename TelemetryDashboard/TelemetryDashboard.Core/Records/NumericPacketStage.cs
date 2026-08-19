using System;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Records;

/// <summary>
/// The pipeline stage that hands numeric records to the existing telemetry path.
/// </summary>
/// <remarks>
/// One adapter, and every consumer already written against <see cref="TelemetryPacket"/> — the
/// analytics engine, the Gorilla compressor, the DVR recorder, the SQLite logger, the scope —
/// becomes reachable from the universal path without being modified or even recompiled.
///
/// It declines everything that is not numeric, which is what keeps that machinery honest: a text
/// or blob record never reaches a stage that would have to invent a magnitude for it.
/// </remarks>
public sealed class NumericPacketStage : IRecordStage
{
    private readonly Func<TelemetryPacket, DataRecord, CancellationToken, ValueTask> _consume;

    /// <summary>
    /// Forwards the packet together with the record it came from.
    /// </summary>
    /// <remarks>
    /// The projection is deliberately narrow — <see cref="TelemetryPacket"/> has no field for
    /// <see cref="DataRecord.Source"/> or <see cref="DataRecord.DerivedFrom"/>, so a consumer given
    /// only the packet has lost the answer to "where did this come from". Handing both across means
    /// a consumer that needs provenance can read it instead of reconstructing it from a side channel,
    /// which is how a port name or a projection name quietly becomes wrong.
    /// </remarks>
    /// <param name="name">Stage name for pipeline counters.</param>
    /// <param name="consume">Receives each projected packet and its originating record.</param>
    public NumericPacketStage(string name, Func<TelemetryPacket, DataRecord, CancellationToken, ValueTask> consume)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A stage must be named so its counters are attributable.", nameof(name));
        }

        Name = name;
        _consume = consume ?? throw new ArgumentNullException(nameof(consume));
    }

    /// <summary>Overload for a consumer that does not need the originating record.</summary>
    public NumericPacketStage(string name, Func<TelemetryPacket, CancellationToken, ValueTask> consume)
        : this(name, (packet, _, token) =>
            (consume ?? throw new ArgumentNullException(nameof(consume)))(packet, token))
    {
    }

    /// <summary>Convenience overload for a synchronous consumer.</summary>
    public NumericPacketStage(string name, Action<TelemetryPacket> consume)
        : this(name, (packet, _, _) =>
        {
            (consume ?? throw new ArgumentNullException(nameof(consume)))(packet);
            return ValueTask.CompletedTask;
        })
    {
    }

    public string Name { get; }

    /// <summary>Numeric records whose reading is absent are dropped rather than forwarded.</summary>
    public long UnreadableCount { get; private set; }

    public bool CanHandle(DataValue value) => value.Kind == DataValueKind.Numeric;

    public async ValueTask ProcessAsync(DataRecord record, CancellationToken cancellationToken = default)
    {
        // A NaN reading means the sensor reported nothing. Forwarding it would put a hole into
        // every rolling mean downstream; counting it keeps the hole visible.
        if (record.Value is DataValue.Numeric { IsMeasured: false })
        {
            UnreadableCount++;
            return;
        }

        if (!TelemetryPacketProjection.TryToPacket(record, out TelemetryPacket packet))
        {
            UnreadableCount++;
            return;
        }

        await _consume(packet, record, cancellationToken).ConfigureAwait(false);
    }
}
