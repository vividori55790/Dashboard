namespace TelemetryDashboard.Core.Models;

[Flags]
public enum PacketFlags
{
    None = 0,
    IsHistorical = 1 << 0,
    IsDerived = 1 << 1,
    ChecksumFailed = 1 << 2,
    AlarmExceeded = 1 << 3,
    Simulated = 1 << 4,

    /// <summary>
    /// This host established that the sample describes an instant meaningfully before it arrived.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="IsHistorical"/>, which is a device saying so about its own data.
    /// This is a determination, made against a measured clock offset and its error bar, and it is
    /// absent both when the sample was prompt and when nothing could be established -- so its
    /// absence is not the claim that a sample is current. <see cref="ArrivalAge"/> keeps those two
    /// apart for anything that needs to know which.
    /// </remarks>
    LateArriving = 1 << 5
}

public sealed class TelemetryPacket
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>What the observing node's own clock read, when it sent one and it was usable.</summary>
    /// <remarks>
    /// Null for a device on this machine's own port: there is one clock there and
    /// <see cref="Timestamp"/> already is it. It is meaningful only for a sample that crossed a
    /// network, where two clocks exist and ARCHITECTURE §3 begins.
    /// <para>
    /// Beside <see cref="Timestamp"/> rather than replacing it, deliberately. Placing a remote
    /// sample on this host's timeline requires the offset between the two clocks to be known and
    /// bounded; until it is, a peer three hours out would scatter its data across the chart with
    /// nothing on the chart saying why. Keeping both is what lets the offset be measured at all --
    /// the pair is the observation.
    /// </para>
    /// </remarks>
    public DateTime? ObservedAt { get; set; }

    /// <summary>The epoch the sender stamped this with, when it sent one.</summary>
    /// <remarks>
    /// Read at ingest and not carried further. It exists so the duplicate filter can tell one
    /// sender's counter from another's and from the same sender's counter before a restart; once
    /// admitted, this host stamps its own on the way out, because the idempotence being offered is
    /// per hop.
    /// </remarks>
    public string? SourceEpoch { get; set; }

    /// <summary>The sender's per-node counter, when it sent one.</summary>
    public long? SourceSequence { get; set; }
    public string NodeId { get; set; } = string.Empty;
    public string Variable { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string RawData { get; set; } = string.Empty;
    public PacketFlags Flags { get; set; } = PacketFlags.None;

    public TelemetryPacket() { }

    public TelemetryPacket(string nodeId, string variable, double value, string unit, DateTime? timestamp = null, PacketFlags flags = PacketFlags.None)
    {
        NodeId = nodeId;
        Variable = variable;
        Value = value;
        Unit = unit;
        Timestamp = timestamp ?? DateTime.UtcNow;
        Flags = flags;
    }
}
