namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// What came back when the host tried its own console, before the banner describes it.
/// </summary>
/// <remarks>
/// Three states rather than a bool, because the middle one is the interesting one. A host running
/// with <c>--credential</c> cannot authenticate to itself -- it holds a PBKDF2 derivation, not the
/// password -- so its own probe is refused, and folding that into "failed" made the banner report
/// a healthy listener as unreachable.
/// </remarks>
public enum ConsoleReachedResult
{
    /// <summary>Nothing answered: no connection, or it timed out.</summary>
    NoAnswer,

    /// <summary>The console answered.</summary>
    Answered,

    /// <summary>
    /// The console answered 401, which is the configured behaviour and proves two things at once:
    /// the listener is up, and the gate is in front of it.
    /// </summary>
    AnsweredAndDemandedCredential
}
