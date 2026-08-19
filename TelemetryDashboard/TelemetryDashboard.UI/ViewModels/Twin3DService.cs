using System;

namespace TelemetryDashboard.UI.ViewModels;

/// <summary>
/// View state for the 3D digital-twin viewport: which mesh is present, how it was scaled to fit
/// the camera, and its current orientation.
/// </summary>
/// <remarks>
/// Deliberately free of any renderer type. The viewport control owns the scene graph; keeping this
/// state as plain numbers means the load, normalise and fallback rules can be exercised without a
/// GPU, a window or a message pump — which is where every defect in this area has actually lived.
/// </remarks>
public sealed class Twin3DService
{
    /// <summary>
    /// Largest bounding-box edge, in scene units, a model may occupy. CAD exports arrive in
    /// millimetres, metres or inches with no dependable unit tag, so the same part can be six
    /// orders of magnitude larger than the camera frustum and render as nothing at all. Scaling
    /// everything into a fixed box means the default camera always frames the model.
    /// </summary>
    private const float ViewportExtent = 10.0f;

    /// <summary>Edge length and triangle count of the placeholder cube.</summary>
    private const float FallbackCubeExtent = 1.0f;
    private const int FallbackCubeTriangles = 12;

    /// <summary>Uniform scale applied to bring the mesh inside the viewport box.</summary>
    public float ModelScale { get; private set; } = 1.0f;

    /// <summary>Largest bounding-box edge after normalisation. Never exceeds the viewport box.</summary>
    public float NormalizedBoundingBoxSize { get; private set; }

    /// <summary>True while any mesh, loaded or placeholder, occupies the viewport.</summary>
    public bool HasModel { get; private set; }

    /// <summary>True when the placeholder cube stands in for a model that could not be loaded.</summary>
    public bool IsFallbackModelActive { get; private set; }

    /// <summary>Path of the mesh currently accepted, or null when none is.</summary>
    public string? LoadedModelPath { get; private set; }

    /// <summary>Triangles in the current mesh, derived from the index list.</summary>
    public int TriangleCount { get; private set; }

    /// <summary>Absolute orientation about each axis, in degrees.</summary>
    public double RotationX { get; private set; }

    /// <inheritdoc cref="RotationX"/>
    public double RotationY { get; private set; }

    /// <inheritdoc cref="RotationX"/>
    public double RotationZ { get; private set; }

    /// <summary>
    /// Accepts an STL mesh for display, falling back to a placeholder cube when it is unusable.
    /// </summary>
    /// <remarks>
    /// Returns false and leaves a visible cube behind rather than throwing. A twin viewport that
    /// silently goes blank on a bad file reads as "the link is down" to an operator; a placeholder
    /// says "the model is wrong", which is a different problem with a different fix.
    /// Only the file's validity is decided here — vertex data reaches the service through
    /// <see cref="SetCustomMesh"/> once the renderer has parsed it, keeping mesh parsing, which
    /// belongs to whichever toolkit draws the scene, out of the view state.
    /// </remarks>
    public bool LoadModel(string path)
    {
        if (!StlFileProbe.IsUsableStl(path))
        {
            ActivateFallbackModel();
            return false;
        }

        LoadedModelPath = path;
        HasModel = true;
        IsFallbackModelActive = false;
        return true;
    }

    /// <summary>
    /// Replaces the viewport geometry with a flat array of x, y, z triplets and its index list.
    /// An empty mesh is a legitimate state — a twin with no node selected — so it clears the
    /// viewport instead of throwing, and a trailing partial triplet is ignored because two thirds
    /// of a coordinate is not a position.
    /// </summary>
    public void SetCustomMesh(float[] vertices, int[] indices)
    {
        int vertexCount = vertices is null ? 0 : vertices.Length / 3;
        if (vertexCount == 0)
        {
            ClearModel();
            return;
        }

        float rawExtent = MeshBounds.LargestAxisExtent(vertices!, vertexCount);
        ModelScale = rawExtent > ViewportExtent ? ViewportExtent / rawExtent : 1.0f;

        // Clamped rather than trusted: scaling by a reciprocal and multiplying back up can land a
        // hair above the box in float precision, and the camera fit depends on this ceiling holding.
        NormalizedBoundingBoxSize = MathF.Min(rawExtent * ModelScale, ViewportExtent);

        TriangleCount = indices is null ? 0 : indices.Length / 3;
        HasModel = true;
        IsFallbackModelActive = false;
    }

    /// <summary>Empties the viewport.</summary>
    /// <remarks>
    /// Orientation is left alone. Clearing one model to load another is the common case, and
    /// resetting the camera each time would discard the viewing angle the operator just set up.
    /// </remarks>
    public void ClearModel()
    {
        LoadedModelPath = null;
        HasModel = false;
        IsFallbackModelActive = false;
        ModelScale = 1.0f;
        NormalizedBoundingBoxSize = 0f;
        TriangleCount = 0;
    }

    /// <summary>
    /// Sets the absolute orientation of the model in degrees.
    /// </summary>
    /// <remarks>
    /// Assigns rather than accumulates: the trackball and the animation timeline each report an
    /// absolute angle, so an accumulating setter drifts out of step with whichever is not driving.
    /// Angles stay unwrapped so a caller sweeping past 360 degrees keeps a monotonic value to
    /// interpolate against; reducing to a principal range is the renderer's job.
    /// </remarks>
    public void Rotate(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)) return;

        RotationX = x;
        RotationY = y;
        RotationZ = z;
    }

    private void ActivateFallbackModel()
    {
        LoadedModelPath = null;
        HasModel = true;
        IsFallbackModelActive = true;
        ModelScale = 1.0f;
        NormalizedBoundingBoxSize = FallbackCubeExtent;
        TriangleCount = FallbackCubeTriangles;
    }
}
