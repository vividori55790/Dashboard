using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace TelemetryDashboard.UI.ViewModels;

/// <summary>
/// Draws a <see cref="TwinThermalField"/>: a plan under the machine, and a marker per sensor.
/// </summary>
/// <remarks>
/// The plan is one mesh whose vertices carry texture coordinates into a gradient brush, rather than
/// several hundred separately coloured tiles. Same picture, one draw call instead of six hundred,
/// and the surface is interpolated by the graphics card between sample points instead of stepping.
/// <para>
/// Both the ramp and the markers take their colours from the same
/// <see cref="HeatmapInterpolationService"/> instance inside the field, so a marker can never
/// disagree with the surface it is standing on about what a temperature looks like.
/// </para>
/// </remarks>
public static class TwinThermalVisual
{
    /// <summary>Radius of a sensor marker, in twin units.</summary>
    private const double MarkerRadius = 0.32;

    /// <summary>Everything that draws the field, ready to be dropped into a viewport.</summary>
    public static IEnumerable<Visual3D> Build(TwinThermalField field)
    {
        if (field is null || !field.HasField) yield break;

        yield return BuildPlan(field);
        foreach (Visual3D marker in BuildMarkers(field)) yield return marker;
    }

    /// <summary>The interpolated surface, as one textured mesh over the sensors' footprint.</summary>
    public static ModelVisual3D BuildPlan(TwinThermalField field)
    {
        (double minX, double maxX, double minZ, double maxZ, double y) = field.Footprint();
        int steps = TwinThermalField.GridResolution;
        var mesh = new MeshGeometry3D();

        for (int iz = 0; iz <= steps; iz++)
        {
            double z = minZ + (maxZ - minZ) * iz / steps;
            for (int ix = 0; ix <= steps; ix++)
            {
                double x = minX + (maxX - minX) * ix / steps;
                mesh.Positions.Add(new Point3D(x, y, z));

                // v is fixed: the ramp is one-dimensional, so only u carries the temperature.
                mesh.TextureCoordinates.Add(new Point(field.NormalisedAt(x, y, z), 0.5));
            }
        }

        int stride = steps + 1;
        for (int iz = 0; iz < steps; iz++)
        {
            for (int ix = 0; ix < steps; ix++)
            {
                int corner = iz * stride + ix;
                mesh.TriangleIndices.Add(corner);
                mesh.TriangleIndices.Add(corner + stride);
                mesh.TriangleIndices.Add(corner + 1);

                mesh.TriangleIndices.Add(corner + 1);
                mesh.TriangleIndices.Add(corner + stride);
                mesh.TriangleIndices.Add(corner + stride + 1);
            }
        }

        // Emissive as well as diffuse: the plan is a readout, and a readout whose colour depends on
        // where the lights happen to be is a readout that lies about the temperature.
        var brush = RampBrush(field);
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(brush));
        material.Children.Add(new EmissiveMaterial(brush));

        return new ModelVisual3D
        {
            Content = new GeometryModel3D
            {
                Geometry = mesh,
                Material = material,
                BackMaterial = material
            }
        };
    }

    /// <summary>One sphere per sensor, at its declared place, in the colour of its own reading.</summary>
    public static IEnumerable<Visual3D> BuildMarkers(TwinThermalField field)
    {
        foreach (TwinThermalReading reading in field.Readings)
        {
            HeatColor colour = field.ColorOf(reading);
            yield return new SphereVisual3D
            {
                Center = new Point3D(reading.Placement.X, reading.Placement.Y, reading.Placement.Z),
                Radius = MarkerRadius,
                Fill = new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B))
            };
        }
    }

    /// <summary>The gradient the plan's texture coordinates index into.</summary>
    private static LinearGradientBrush RampBrush(TwinThermalField field)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };

        IReadOnlyList<HeatColor> ramp = field.Ramp();
        for (int i = 0; i < ramp.Count; i++)
        {
            brush.GradientStops.Add(new GradientStop(
                Color.FromRgb(ramp[i].R, ramp[i].G, ramp[i].B),
                ramp.Count == 1 ? 0.0 : (double)i / (ramp.Count - 1)));
        }

        brush.Freeze();
        return brush;
    }
}
