using System;

namespace TelemetryDashboard.UI.ViewModels;

/// <summary>
/// Axis-aligned bounding-box measurements over a flat vertex array of x, y, z triplets.
/// </summary>
/// <remarks>
/// Split from <see cref="Twin3DService"/> so the geometry arithmetic can be reasoned about on its
/// own: the service decides what to do with an extent, this decides what the extent is.
/// </remarks>
internal static class MeshBounds
{
    /// <summary>
    /// Longest edge of the bounding box enclosing <paramref name="vertexCount"/> vertices.
    /// </summary>
    /// <remarks>
    /// Non-finite coordinates are skipped rather than allowed to widen the box to infinity, which
    /// would drive any derived scale factor to zero and collapse the model to a point — turning one
    /// corrupt vertex into a blank viewport. A single-vertex mesh has a genuinely zero extent
    /// however far from the origin it sits, since extent measures size, not position.
    /// </remarks>
    public static float LargestAxisExtent(float[] vertices, int vertexCount)
    {
        Span<float> min = stackalloc float[3] { float.MaxValue, float.MaxValue, float.MaxValue };
        Span<float> max = stackalloc float[3] { float.MinValue, float.MinValue, float.MinValue };

        for (int vertex = 0; vertex < vertexCount; vertex++)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                float coordinate = vertices[vertex * 3 + axis];
                if (!float.IsFinite(coordinate)) continue;

                if (coordinate < min[axis]) min[axis] = coordinate;
                if (coordinate > max[axis]) max[axis] = coordinate;
            }
        }

        float extent = 0f;
        for (int axis = 0; axis < 3; axis++)
        {
            if (min[axis] <= max[axis]) extent = MathF.Max(extent, max[axis] - min[axis]);
        }
        return extent;
    }
}
