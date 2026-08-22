using System;
using System.Linq;
using FluentAssertions;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.UI.ViewModels;
using Xunit;

namespace TelemetryDashboard.Tests.Desktop;

/// <summary>
/// The temperature field the digital twin paints, and the property that makes it trustworthy.
/// </summary>
/// <remarks>
/// <see cref="HeatmapInterpolationService"/> shipped complete — inverse-distance weighted, with a
/// written defence of that choice — and was constructed by nothing, so a panel labelled "digital
/// twin" showed a box and no state at all.
/// <para>
/// Measured on the running application after wiring, on the bundled power-converter profile:
/// "2 sensors · 39.2–43.1 °C · hottest PSFB 서버 레일" at rest, "43.2–87.4 °C · hottest DAB 배터리
/// 컨버터" with the DAB overcurrent fault injected, and back again on recovery. The hot spot moved
/// to the other board, which is the thing the panel exists to show.
/// </para>
/// </remarks>
public class TwinThermalFieldTests
{
    private static TwinThermalReading At(string id, double x, double y, double z, double celsius) => new()
    {
        NodeId = id,
        Label = id,
        Placement = new SensorPlacement { X = x, Y = y, Z = z },
        Celsius = celsius
    };

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void TheFieldReadsBackExactlyWhatEachSensorReported()
    {
        // Inverse-distance weighting is exact at its own inputs by construction, and that is the
        // entire argument for it over a fitted surface: the reading an operator can walk over and
        // check against a hand-held probe is the one that must never be wrong.
        var field = new TwinThermalField();
        field.Update(new[] { At("a", -3, 0.6, 0, 40.0), At("b", 3, 0.6, 0, 88.5) });

        field.At(-3, 0.6, 0).Should().Be(40.0);
        field.At(3, 0.6, 0).Should().Be(88.5);
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void BetweenTwoSensorsTheFieldSitsBetweenTheirReadings()
    {
        var field = new TwinThermalField();
        field.Update(new[] { At("a", -3, 0, 0, 40.0), At("b", 3, 0, 0, 90.0) });

        field.At(0, 0, 0).Should().BeApproximately(65.0, 1e-9, "equidistant, so equally weighted");
        field.At(-2, 0, 0).Should().BeInRange(40.0, 65.0, "nearer the cold sensor");
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void TheGradientIsStretchedToTheReadingsPresentRatherThanPinnedToAFixedSpan()
    {
        // A fixed 0-100 range paints a rig running between 40 and 44 degrees in one flat colour --
        // which is exactly the case where an operator most wants to see which board is warmer.
        var field = new TwinThermalField();
        field.Update(new[] { At("a", -3, 0, 0, 40.0), At("b", 3, 0, 0, 44.0) });

        field.Bounds.Should().Be((40.0, 44.0));
        field.NormalisedAt(-3, 0, 0).Should().BeApproximately(0.0, 1e-9);
        field.NormalisedAt(3, 0, 0).Should().BeApproximately(1.0, 1e-9);
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void AUniformFieldIsPaintedUniformlyRatherThanDividedByZero()
    {
        var field = new TwinThermalField();
        field.Update(new[] { At("a", -3, 0, 0, 42.0), At("b", 3, 0, 0, 42.0) });

        field.NormalisedAt(0, 0, 0).Should().Be(0.0);
        field.At(0, 0, 0).Should().BeApproximately(42.0, 1e-9,
            "the honest picture of a machine at one temperature is one temperature");
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void SwitchingProfilesDoesNotLeaveThePreviousRigsSensorsInTheField()
    {
        // The defect wiring exposed. HeatmapInterpolationService keys its sensors by coordinate and
        // had no way to forget them, so a live rig re-reporting the same points was fine and
        // changing profiles was not: the old machine's sensors stayed, and the result is not a
        // stale reading -- which someone might notice -- but an interpolation between two different
        // machines, which looks exactly like a real gradient.
        var field = new TwinThermalField();
        field.Update(new[] { At("old", -9, 0, 0, 95.0), At("old2", -8, 0, 0, 95.0) });

        field.Update(new[] { At("new", 3, 0, 0, 40.0), At("new2", 4, 0, 0, 41.0) });

        field.Readings.Should().HaveCount(2);
        field.Bounds.Should().Be((40.0, 41.0), "95 belonged to a machine that is no longer here");
        field.At(-9, 0, 0).Should().BeLessThan(50.0, "nothing at that point reports 95 any more");
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void TheHottestDeviceIsNamedRatherThanLeftForTheOperatorToFind()
    {
        var field = new TwinThermalField();
        field.Update(new[] { At("COM3", -3, 0, 0, 87.4), At("COM4", 3, 0, 0, 43.2) });

        field.Hottest!.NodeId.Should().Be("COM3");
        field.Summary().Should().Contain("2 sensors").And.Contain("87.4");
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void AReadingWithNoUsableTemperatureIsDroppedRatherThanPlaced()
    {
        var field = new TwinThermalField();
        field.Update(new[] { At("good", 0, 0, 0, 42.0), At("bad", 5, 0, 0, double.NaN) });

        field.Readings.Should().ContainSingle().Which.NodeId.Should().Be("good");
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void WithNoSensorsThePanelSaysSoInsteadOfDrawingAnEmptyPlan()
    {
        var field = new TwinThermalField();
        field.Update(Array.Empty<TwinThermalReading>());

        field.HasField.Should().BeFalse();
        field.Hottest.Should().BeNull();
        field.Summary().Should().Contain("온도 센서 없음");
        TwinThermalVisual.Build(field).Should().BeEmpty();
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void ThePlanCoversTheSensorsAndABitBeyondThem()
    {
        // Stopping exactly at the sensors puts the hottest part of a board on the boundary, which
        // reads as the field being cut off rather than as the board being at the edge of what is
        // measured.
        var field = new TwinThermalField();
        field.Update(new[] { At("a", -3, 0.6, -1, 40.0), At("b", 3, 0.6, 1, 44.0) });

        (double minX, double maxX, double minZ, double maxZ, double y) = field.Footprint();

        minX.Should().BeLessThan(-3);
        maxX.Should().BeGreaterThan(3);
        minZ.Should().BeLessThan(-1);
        maxZ.Should().BeGreaterThan(1);
        y.Should().Be(0.6);
    }

    // ---- what gets drawn ------------------------------------------------------

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void ThePlanIsOneTexturedMeshWithACoordinatePerVertex()
    {
        var field = new TwinThermalField();
        field.Update(new[] { At("a", -3, 0, 0, 40.0), At("b", 3, 0, 0, 90.0) });

        var plan = TwinThermalVisual.BuildPlan(field);
        var mesh = (System.Windows.Media.Media3D.MeshGeometry3D)
            ((System.Windows.Media.Media3D.GeometryModel3D)plan.Content).Geometry;

        int side = TwinThermalField.GridResolution + 1;
        mesh.Positions.Count.Should().Be(side * side);
        mesh.TextureCoordinates.Count.Should().Be(side * side,
            "a vertex with no coordinate samples the gradient at zero and paints a cold hole");
        mesh.TriangleIndices.Count.Should().Be(TwinThermalField.GridResolution * TwinThermalField.GridResolution * 6);
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void EverySensorGetsAMarkerInTheColourItsOwnReadingMapsTo()
    {
        var field = new TwinThermalField();
        field.Update(new[] { At("cold", -3, 0, 0, 40.0), At("hot", 3, 0, 0, 90.0) });

        var markers = TwinThermalVisual.BuildMarkers(field)
            .Cast<HelixToolkit.Wpf.SphereVisual3D>().ToArray();

        markers.Should().HaveCount(2);
        foreach (HelixToolkit.Wpf.SphereVisual3D marker in markers)
        {
            TwinThermalReading reading = field.Readings.Single(r => Math.Abs(r.Placement.X - marker.Center.X) < 1e-9);
            HeatColor expected = field.ColorOf(reading);
            var brush = (System.Windows.Media.SolidColorBrush)marker.Fill;

            brush.Color.R.Should().Be(expected.R);
            brush.Color.G.Should().Be(expected.G);
            brush.Color.B.Should().Be(expected.B);
        }
    }
}
