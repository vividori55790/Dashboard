using System.Collections.Generic;
using System.Windows.Media.Media3D;

namespace TelemetryDashboard.UI.ViewModels;

/// <summary>
/// Flattens a parsed scene into the plain vertex and index arrays <see cref="Twin3DService"/> reads.
/// </summary>
/// <remarks>
/// The seam that lets the view state stay free of any renderer type. <see cref="Twin3DService"/>
/// documents that mesh parsing "belongs to whichever toolkit draws the scene" and takes only
/// <c>float[]</c> and <c>int[]</c>; this is the one place that knows both sides, and it is
/// deliberately the only one.
/// <para>
/// Transforms are composed down the tree rather than ignored. An STL usually parses to a single
/// untransformed mesh, so ignoring them appears to work — until a scene with a placed sub-assembly
/// reports the bounding box of the parts sitting on top of each other at the origin, and the model
/// is normalised by a scale computed from a size it does not have.
/// </para>
/// <para>
/// Public, unlike its two siblings <c>StlFileProbe</c> and <c>MeshBounds</c>, which are internal
/// and reached through <see cref="Twin3DService"/>'s own API. This one is reached only from the
/// panel's code-behind, and that control calls <c>FindResource</c> during a load, so constructing
/// it outside a running application throws before any assertion could be made. Internal here would
/// mean the transform composition and the missing-index-list rule -- the two parts most worth
/// pinning -- could not be tested at all.
/// </remarks>
public static class TwinMeshFlattener
{
    /// <summary>Every triangle in <paramref name="model"/>, in one vertex array and one index array.</summary>
    public static (float[] Vertices, int[] Indices) Flatten(Model3D? model)
    {
        var vertices = new List<float>();
        var indices = new List<int>();
        Collect(model, Transform3D.Identity, vertices, indices);

        return (vertices.ToArray(), indices.ToArray());
    }

    private static void Collect(Model3D? model, Transform3D inherited, List<float> vertices, List<int> indices)
    {
        if (model is null) return;

        Transform3D combined = Compose(inherited, model.Transform);

        if (model is Model3DGroup group)
        {
            foreach (Model3D child in group.Children) Collect(child, combined, vertices, indices);
            return;
        }

        if (model is not GeometryModel3D geometry || geometry.Geometry is not MeshGeometry3D mesh) return;

        int baseVertex = vertices.Count / 3;
        foreach (Point3D point in mesh.Positions)
        {
            Point3D placed = combined.Transform(point);
            vertices.Add((float)placed.X);
            vertices.Add((float)placed.Y);
            vertices.Add((float)placed.Z);
        }

        if (mesh.TriangleIndices.Count > 0)
        {
            foreach (int index in mesh.TriangleIndices) indices.Add(baseVertex + index);
            return;
        }

        // A mesh with no index list is taken as consecutive triples, which is what WPF itself does
        // when TriangleIndices is empty. Without this a binary STL -- which carries no index list
        // at all -- flattens to zero triangles and the panel reports "holds no geometry" for a
        // model it read perfectly well.
        for (int i = 0; i < mesh.Positions.Count; i++) indices.Add(baseVertex + i);
    }

    /// <summary>Applies a child's own transform on top of the one it inherited.</summary>
    private static Transform3D Compose(Transform3D inherited, Transform3D? own)
    {
        if (own is null || own == Transform3D.Identity) return inherited;
        if (inherited == Transform3D.Identity) return own;

        var composed = new Transform3DGroup();
        composed.Children.Add(own);
        composed.Children.Add(inherited);
        return composed;
    }
}
