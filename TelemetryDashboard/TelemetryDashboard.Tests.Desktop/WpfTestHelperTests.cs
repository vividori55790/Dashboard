using TelemetryDashboard.Tests.Desktop.TestUtilities;

namespace TelemetryDashboard.Tests.Desktop;

/// <summary>Self-test for the STA thread helper.</summary>
/// <remarks>
/// The one member of <c>TestUtilitiesTests</c> that could not stay in the portable suite.
/// <c>Thread.SetApartmentState</c> is a Windows-only API — on Linux it throws
/// <c>PlatformNotSupportedException</c> rather than returning a non-STA thread — so this assertion
/// is not merely WPF-flavoured, it is unrunnable off Windows. Its former siblings, which build
/// telemetry frames out of strings and bytes, stayed behind.
/// </remarks>
public class WpfTestHelperTests
{
    [WpfTestFact]
    public void WpfTestHelper_RunOnStaThread_ExecutesOnStaApartmentState()
    {
        bool isSta = false;
        WpfTestHelper.RunOnStaThread(() =>
        {
            isSta = Thread.CurrentThread.GetApartmentState() == ApartmentState.STA;
        });

        isSta.Should().BeTrue();
    }
}
