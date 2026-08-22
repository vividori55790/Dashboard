using System;
using System.IO;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace TelemetryDashboard.UI.ViewModels;

/// <summary>What came out of a model file: the scene, its geometry, or the reason there is none.</summary>
public sealed class TwinModelLoad
{
    /// <summary>The parsed scene, or null when the file could not be read.</summary>
    public Model3DGroup? Scene { get; init; }

    /// <summary>Vertices as x, y, z triplets.</summary>
    public float[] Vertices { get; init; } = Array.Empty<float>();

    /// <summary>Triangle indices into <see cref="Vertices"/>.</summary>
    public int[] Indices { get; init; } = Array.Empty<int>();

    /// <summary>Why there is no usable mesh, or null when there is one.</summary>
    public string? Failure { get; init; }

    /// <summary>Whether there is geometry to put in a viewport.</summary>
    public bool Succeeded => Scene is not null && Failure is null && Vertices.Length > 0;
}

/// <summary>
/// Turns a path into geometry, or into the sentence explaining why it is not geometry.
/// </summary>
/// <remarks>
/// Split out of the panel's code-behind because reading a file and arranging a viewport are two
/// jobs, and only one of them can be tested: the control calls <c>FindResource</c> while it works,
/// so constructing it outside a running application throws before any assertion could be made.
/// Every failure this reports was reachable in the running program and none of them could be
/// reproduced in a test until this existed.
/// <para>
/// Nothing here throws. A model file is operator input — dragged in, downloaded, half-copied — and
/// a bad one is an ordinary outcome, not an exceptional one. It used to be exceptional in the worst
/// sense: the load runs from the panel's <c>Loaded</c> handler, so an unhandled parse error would
/// have come out on the dispatcher and taken the whole dashboard down the first time anyone opened
/// the twin. A malformed .stl sitting beside the executable was enough.
/// </para>
/// </remarks>
public static class TwinModelLoader
{
    /// <summary>Reads and flattens <paramref name="path"/>.</summary>
    public static TwinModelLoad Read(string path)
    {
        Model3DGroup? scene;
        try
        {
            scene = new StLReader().Read(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or FormatException or InvalidDataException
                                      or ArgumentException or OverflowException
                                      or IndexOutOfRangeException or NotSupportedException)
        {
            // The type name, not the message. A reader's message names a line number in a file the
            // operator did not write; the kind of failure is what tells them whether to re-export
            // the model or to check the disk.
            return new TwinModelLoad { Failure = $"could not be parsed: {ex.GetType().Name}" };
        }

        if (scene is null) return new TwinModelLoad { Failure = "could not be parsed: the reader returned nothing" };

        (float[] vertices, int[] indices) = TwinMeshFlattener.Flatten(scene);
        if (vertices.Length == 0) return new TwinModelLoad { Failure = "holds no geometry" };

        return new TwinModelLoad { Scene = scene, Vertices = vertices, Indices = indices };
    }
}
