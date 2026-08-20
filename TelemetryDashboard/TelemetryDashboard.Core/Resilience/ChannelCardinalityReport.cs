using System.Globalization;

namespace TelemetryDashboard.Core.Resilience;

/// <summary>
/// What a bounded per-channel store is currently holding, and what it is allowed to hold.
/// </summary>
/// <remarks>
/// The point of publishing this is that an operator can watch the system approach its limit instead
/// of discovering the limit as an out-of-memory crash. A store that silently drops channels once it
/// is full looks identical, from the outside, to one that is comfortably under-subscribed; the
/// difference is <see cref="Evictions"/>.
/// </remarks>
public readonly record struct ChannelCardinalityReport(
    string Subject,
    int Live,
    int Capacity,
    long Evictions,
    int EvictionRecordCapacity)
{
    /// <summary>Fraction of the ceiling currently occupied, 0..1.</summary>
    public double Utilisation => Capacity <= 0 ? 0.0 : (double)Live / Capacity;

    /// <summary>True once the store is full, which is the point from which every new channel costs an old one.</summary>
    public bool AtCapacity => Capacity > 0 && Live >= Capacity;

    /// <summary>
    /// True when this store has discarded channel state. Any verdict history for those channels is
    /// gone, so the absence of a recent anomaly on them means nothing.
    /// </summary>
    public bool HasEvicted => Evictions > 0;

    /// <summary>Channels that can still be admitted before eviction starts.</summary>
    public int Headroom => Math.Max(0, Capacity - Live);

    public override string ToString()
    {
        string summary = string.Create(CultureInfo.InvariantCulture,
            $"{Subject}: {Live:N0}/{Capacity:N0} channels ({Utilisation:P1}), {Evictions:N0} evicted");

        return AtCapacity
            ? summary + " - AT CAPACITY, admitting a new channel now discards the least recently updated one"
            : summary;
    }
}
