namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// What the derived-channel pump has actually done, for <c>/api/status</c> to report.
/// </summary>
/// <remarks>
/// An interface rather than fields on the server because the pump lives in the host and the status
/// endpoint lives here, and neither should have to know the other's type.
/// <para>
/// This exists because the pump went quiet mid-run and nothing anywhere could say whether it had
/// stopped, was refusing every instant, or had never had a channel to compute. Those three have the
/// same symptom — a channel that has stopped arriving — and completely different causes. A count of
/// what was published, what was withheld and what threw separates them without a debugger.
/// </para>
/// </remarks>
public interface IComputedChannelCounters
{
    /// <summary>Derived samples that reached the publisher.</summary>
    long Published { get; }

    /// <summary>Instants that existed but no value could be computed for.</summary>
    long Withheld { get; }

    /// <summary>Channels abandoned after throwing.</summary>
    long Faulted { get; }

    /// <summary>The first fault seen, or null.</summary>
    string? FaultMessage { get; }
}
