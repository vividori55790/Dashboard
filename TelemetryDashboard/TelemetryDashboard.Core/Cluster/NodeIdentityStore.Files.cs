using System;
using System.IO;

namespace TelemetryDashboard.Core.Cluster;

/// <summary>
/// The filesystem half of the identity store: reading a file that may not be there, and writing
/// one that may not be allowed.
/// </summary>
/// <remarks>
/// Split out when the store crossed the 150-line rule, and split here because it is a genuinely
/// different concern: everything in the other file is about what an identity means, and everything
/// in this one is about a disk that can refuse. Both operations answer "no" rather than throwing,
/// because a host that will not start over a read-only profile directory is worse than one that
/// runs with an identity for this session and says so.
/// </remarks>
public static partial class NodeIdentityStore
{
    private static string ReadIfValid(string path)
    {
        try
        {
            if (!File.Exists(path)) return string.Empty;

            string text = File.ReadAllText(path).Trim();
            return NodeIdentity.IsValidId(text) ? text : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static bool TryWrite(string path, string id)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(path, id + Environment.NewLine);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
