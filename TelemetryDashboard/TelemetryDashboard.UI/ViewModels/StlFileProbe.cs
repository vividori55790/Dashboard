using System;
using System.IO;
using System.Text;

namespace TelemetryDashboard.UI.ViewModels;

/// <summary>
/// Cheap structural check that a file on disk really is an STL mesh a renderer can parse.
/// </summary>
/// <remarks>
/// Separated from <see cref="Twin3DService"/> because it is the only part of model loading that
/// touches the file system, and it is the part that has to keep working against hostile input:
/// truncated downloads, files renamed from another format, and text saved with a mesh extension.
/// </remarks>
internal static class StlFileProbe
{
    /// <summary>Bytes of an STL header: 80-byte comment plus the 4-byte triangle count.</summary>
    private const int HeaderLength = 84;

    /// <summary>Bytes each triangle occupies in a binary STL: 12 floats plus a 2-byte attribute.</summary>
    private const long BytesPerTriangle = 50;

    /// <summary>
    /// True when <paramref name="path"/> points at a file whose contents are shaped like an STL.
    /// </summary>
    /// <remarks>
    /// The extension alone is not enough — the failure worth catching is a file that was renamed or
    /// cut short — so the header is sniffed as well. ASCII STL opens with the token "solid"; binary
    /// STL is exactly 84 bytes plus 50 per declared triangle, which is a strong enough check that a
    /// truncated download fails it.
    /// <para>
    /// I/O faults are answered with false rather than an exception. Every caller here has the same
    /// recovery, showing a placeholder mesh, and a locked or vanished file is an ordinary outcome
    /// when the path came from a recent-files list.
    /// </para>
    /// </remarks>
    public static bool IsUsableStl(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        if (!Path.GetExtension(path).Equals(".stl", StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            using FileStream stream = File.OpenRead(path);
            byte[] header = new byte[HeaderLength];
            if (stream.Read(header, 0, HeaderLength) < HeaderLength) return false;

            if (Encoding.ASCII.GetString(header, 0, 5).Equals("solid", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            uint declaredTriangles = BitConverter.ToUInt32(header, 80);
            return stream.Length == HeaderLength + BytesPerTriangle * declaredTriangles;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
