namespace TelemetryDashboard.Tests.Desktop.Tiers.Tier2_BoundaryCornerCases;

/// <summary>
/// F17's five boundary tests are gone with the class they exercised.
/// </summary>
/// <remarks>
/// They asked their questions of <c>ScopeViewModel</c>, which no scope ever used. It guarded every
/// buffer with a lock, on the stated premise that "samples arrive on serial and parser threads
/// while the dispatcher clears and re-reads the same buffers" — and that is not what the running
/// control does. <c>ScopeViewControl</c> never lets an ingest thread near its buffers at all: a
/// push enqueues onto a ConcurrentQueue and a dispatcher timer is the only thing that drains it,
/// so there is nothing for a lock to protect and no contention on the render path.
/// <para>
/// The one idea worth taking survived. The view model held a valid-point count beside a total one
/// and documented the pair as a decode-health indicator; the running scope counted nothing at all,
/// down three separate discard paths. <c>ScopeDropAccountingTests</c> covers what replaced it, and
/// the buffer questions are asked of <c>ScopeChannelSeries</c>, which is what the scope really uses.
/// </para>
/// </remarks>
public static class F17_ScopeViewModelBoundaryTests
{
}
