namespace TelemetryDashboard.Core.Resilience;

/// <summary>
/// How a channel came to be resident in a <see cref="BoundedChannelRegistry{TState}"/> on this call.
/// </summary>
/// <remarks>
/// This exists so that eviction is never silent. A bounded registry discards state, and state that
/// was discarded and then rebuilt is not the same thing as state that has been accumulating since
/// the channel appeared — the second carries a history, the first does not. A caller that cannot
/// tell them apart will present a freshly reset channel as though its numbers meant something.
/// </remarks>
public enum ChannelAdmission
{
    /// <summary>The channel's state was already resident and was returned unchanged.</summary>
    Existing = 0,

    /// <summary>
    /// State was created for a channel the registry has no record of having evicted.
    /// </summary>
    /// <remarks>
    /// This does not prove the channel is new. The eviction record is itself bounded — see
    /// <see cref="BoundedChannelRegistry{TState}.EvictionRecordCapacity"/> — so a channel evicted
    /// further back than that record reaches is admitted as <see cref="Admitted"/> rather than as
    /// <see cref="ReadmittedAfterEviction"/>. The registry's eviction counter is the figure that is
    /// always exact; this flag is a best-effort attribution to a specific name.
    /// </remarks>
    Admitted = 1,

    /// <summary>
    /// State was created for a channel the registry evicted earlier. Its previous history is gone
    /// and everything derived from it restarts from nothing.
    /// </summary>
    ReadmittedAfterEviction = 2
}
