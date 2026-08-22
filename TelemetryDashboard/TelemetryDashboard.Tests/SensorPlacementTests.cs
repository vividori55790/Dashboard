using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Where a profile says its devices sit, and what happens when it half-says.
/// </summary>
/// <remarks>
/// Geometry belongs to the profile for the reason <see cref="ProfileNode"/> exists at all: the
/// control panel once named two of one customer's devices in XAML, so every other installation was
/// offered power switches for hardware it did not own. A rig with its converters stacked and a rig
/// with them side by side are different machines, and only the profile knows which is in front of
/// the operator.
/// </remarks>
public class SensorPlacementTests
{
    private static MonitoringProfileSet Read(string nodesJson)
    {
        string json = $$"""
            {
              "profiles": [
                {
                  "id": "placed-rig",
                  "displayName": "Placed rig",
                  "nodes": [ {{nodesJson}} ],
                  "channels": [
                    { "id": "t", "label": "Temp", "unit": "°C",
                      "minimum": 0, "maximum": 100, "nominal": 25, "decimals": 1 }
                  ]
                }
              ]
            }
            """;

        string dir = Path.Combine(Path.GetTempPath(), "tdplace_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, MonitoringProfileStore.FileName), json);
            return MonitoringProfileStore.Load(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void APlacementIsCarriedOffTheProfileFileIntoTheNode()
    {
        MonitoringProfileSet set = Read(
            """{ "id": "COM3", "label": "DAB", "placement": { "x": -3, "y": 0.6, "z": 0 } }""");

        ProfileNode node = set.Profiles.Single(p => p.Id == "placed-rig").Nodes.Single();
        node.Placement.Should().NotBeNull();
        node.Placement!.X.Should().Be(-3);
        node.Placement.Y.Should().Be(0.6);
        node.Placement.Z.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ANodeWithNoPlacementIsPerfectlyOrdinary()
    {
        // Most profiles will never place anything. The twin says so rather than guessing, which is
        // the whole reason this is nullable instead of defaulting to the origin.
        MonitoringProfileSet set = Read("""{ "id": "COM3", "label": "DAB" }""");

        set.Profiles.Single(p => p.Id == "placed-rig").Nodes.Single().Placement.Should().BeNull();
    }

    [Theory]
    [Trait("Category", "Tier1")]
    [InlineData("""{ "x": 1, "y": 2 }""", "z missing")]
    [InlineData("""{ "x": 1, "z": 2 }""", "y missing")]
    [InlineData("""{ "y": 1, "z": 2 }""", "x missing")]
    [InlineData("""{ }""", "all three missing")]
    public void AHalfWrittenPlacementIsRefusedRatherThanCompleted(string placement, string why)
    {
        // Defaulting the missing axis to zero would put the device on the floor at the origin, and
        // the twin would draw it there exactly as confidently as it draws a correct one.
        MonitoringProfileSet set = Read($$"""{ "id": "COM3", "label": "DAB", "placement": {{placement}} }""");

        set.Profiles.Should().NotContain(p => p.Id == "placed-rig", why);
        set.Message.Should().Contain("placement");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ANonFinitePlacementIsRefused()
    {
        // JSON has no NaN literal, but a very large exponent overflows to infinity on parse, and an
        // infinite coordinate drives every derived scale factor to zero -- a blank viewport from
        // one bad number.
        MonitoringProfileSet set = Read(
            """{ "id": "COM3", "label": "DAB", "placement": { "x": 1e400, "y": 0, "z": 0 } }""");

        set.Profiles.Should().NotContain(p => p.Id == "placed-rig");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheBundledPowerConverterProfilePlacesBothConvertersInParallel()
    {
        // This rig has the DAB and the PSFB hanging off the high-voltage DC bus in parallel, so
        // they sit side by side at the same height rather than one downstream of the other. The
        // digital twin draws what this says, so what it says has to stay true.
        MonitoringProfile power = MonitoringProfileStore.Load(AppContext.BaseDirectory).Profiles
            .Single(p => p.DisplayName.Contains("DAB/PSFB", StringComparison.Ordinal));

        ProfileNode dab = power.Nodes.Single(n => n.Id == "COM3");
        ProfileNode psfb = power.Nodes.Single(n => n.Id == "COM4");

        dab.Placement.Should().NotBeNull();
        psfb.Placement.Should().NotBeNull();
        dab.Placement!.Y.Should().Be(psfb.Placement!.Y, "parallel on the bus, not stacked");
        dab.Placement.X.Should().NotBe(psfb.Placement.X, "two boards cannot occupy one point");
    }
}
