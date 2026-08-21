using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The collection that measurement-sensitive tests belong to, so they never run alongside each
/// other.
/// </summary>
/// <remarks>
/// xUnit runs test collections in parallel, and every class without an explicit collection gets its
/// own. That is right for the great majority of this suite and wrong for the handful of tests whose
/// assertion <em>is</em> a measurement: throughput, wall clock, bytes allocated. Two of those
/// running at once measure each other.
/// <para>
/// It cost real time to work that out. Over several full runs the failures moved around — a storage
/// benchmark, then a debounce test, then a JavaScript load, then a downsample allocation, then a
/// streaming throughput — never the same one twice, each passing alone. Chasing them individually
/// found two genuine defects worth having (a plugin load timeout that was wall-clock bound, and a
/// debounce test that slept inside the window it was testing), but the residue was always the same
/// story: heavy tests running concurrently.
/// </para>
/// <para>
/// Naming the collection is the fix rather than excluding the tests or widening their bounds. They
/// still all run, and they still assert exactly what they asserted; they simply take turns.
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeavyTestCollection
{
    public const string Name = "measurement-sensitive";
}
