using System;

namespace TelemetryDashboard.Core.Records;

/// <summary>
/// Identifies one series: a named stream and a key within it.
/// </summary>
/// <remarks>
/// Deliberately two parts rather than one dotted string. A telemetry node's channel, a ward's
/// appointment slot and a workbook's cell all decompose the same way — the thing that groups, and
/// the thing that varies inside it — and keeping them separate is what lets routing, indexing and
/// access control operate on the group without parsing.
/// </remarks>
public readonly record struct DataKey(string Stream, string Key)
{
    public override string ToString() => $"{Stream}/{Key}";
}

/// <summary>
/// One observation on the universal path: what was seen, where, when, and where it came from.
/// </summary>
/// <remarks>
/// The provenance fields are not bookkeeping. A record that was computed by a projection and one
/// that a sensor reported look identical once they are both numbers on a chart, and the entire
/// reason this project exists is that a plausible number of unknown origin is worse than a missing
/// one. <see cref="DerivedFrom"/> being null is the assertion "this was measured".
/// </remarks>
public sealed record DataRecord
{
    public required DataKey Key { get; init; }

    /// <summary>When the observation was made. Not the value — see <see cref="DataValue.Instant"/>.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    public required DataValue Value { get; init; }

    /// <summary>Who reported it: a device id, a file name, an integration.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// The projection that computed this record, or <c>null</c> when it was measured directly.
    /// </summary>
    public string? DerivedFrom { get; init; }

    /// <summary>
    /// The original encoding this was decoded from, when the caller chose to retain it.
    /// </summary>
    /// <remarks>
    /// Kept so a disputed value can be traced back to the bytes on the wire. It is provenance, not
    /// payload: nothing downstream may parse it to recover a measurement the decoder did not
    /// produce.
    /// </remarks>
    public string? RawSource { get; init; }

    /// <summary>True when the value was generated rather than measured.</summary>
    /// <remarks>
    /// The same kind of assertion as <see cref="DerivedFrom"/> being null, and for the same
    /// reason: a synthetic reading and a measured one look identical once they are both numbers on
    /// a chart. It lives here rather than only on the packet because this is the boundary the mark
    /// used to die at -- <c>TelemetryPacket</c> carried it in and the projection had nowhere to put
    /// it, so a host relaying a peer's simulator output republished it as measured data.
    /// <para>
    /// False is a claim, not a default nobody thought about. Every source with a cable or a
    /// generator behind it knows which of the two it is.
    /// </para>
    /// </remarks>
    public bool Synthetic { get; init; }

    /// <summary>What the observing node's own clock read, when it crossed a network to get here.</summary>
    /// <remarks>
    /// Null for anything observed on this machine, where <see cref="Timestamp"/> is already that
    /// clock. Non-null only for a sample that arrived from elsewhere carrying a usable reading of
    /// its own -- the pair is what makes the offset between two clocks measurable at all, and
    /// ARCHITECTURE §3 is about what cannot be said until it is.
    /// </remarks>
    public DateTimeOffset? ObservedAt { get; init; }
    /// <summary>True when a projection produced this rather than an instrument.</summary>
    public bool IsDerived => DerivedFrom is not null;

    /// <summary>Convenience constructor for a measured quantity.</summary>
    public static DataRecord Measured(
        string stream, string key, double value, string unit = "",
        DateTimeOffset? timestamp = null, string source = "") => new()
    {
        Key = new DataKey(stream, key),
        Timestamp = timestamp ?? DateTimeOffset.UtcNow,
        Value = new DataValue.Numeric(value, unit),
        Source = source
    };

    /// <summary>
    /// Convenience constructor for a value a projection computed.
    /// </summary>
    /// <param name="derivedFrom">
    /// Names the projection. Required — a derived record that cannot say what produced it is
    /// indistinguishable from a measurement, which is the confusion this field exists to stop.
    /// </param>
    /// <param name="source">
    /// Who reported the observation this was computed from. Carried through rather than left
    /// empty: a derived figure is attributed to the same reporter as its input, because that is
    /// where it came from and because the field is what tells a multi-port rig which cable a
    /// channel arrived on. Measured live before this parameter existed -- every derived channel
    /// published with an empty port beside a measured one reading "SIM".
    /// </param>
    public static DataRecord Derived(
        string stream, string key, DataValue value, string derivedFrom,
        DateTimeOffset? timestamp = null, string source = "")
    {
        if (string.IsNullOrWhiteSpace(derivedFrom))
        {
            throw new ArgumentException(
                "A derived record must name the projection that produced it.", nameof(derivedFrom));
        }

        return new DataRecord
        {
            Key = new DataKey(stream, key),
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Value = value,
            Source = source,
            DerivedFrom = derivedFrom
        };
    }
}
