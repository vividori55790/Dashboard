using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// Serves the bundled web console assets from a confined set of web roots.
/// </summary>
/// <remarks>
/// Every resolved path is canonicalised and re-checked against its root, so a request such as
/// <c>/../../appsettings.json</c> cannot escape. The previous resolver walked up to five parent
/// directories looking for a match, which turned the whole repository tree — keys, logs, source —
/// into web-reachable content on a listener bound to every local interface.
/// </remarks>
public sealed class StaticContentHost
{
    private static readonly IReadOnlyDictionary<string, string> ContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".html"] = "text/html; charset=utf-8",
            [".htm"] = "text/html; charset=utf-8",
            [".js"] = "application/javascript; charset=utf-8",
            [".css"] = "text/css; charset=utf-8",
            [".json"] = "application/json; charset=utf-8",
            [".svg"] = "image/svg+xml",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".ico"] = "image/x-icon",
            [".woff2"] = "font/woff2",
            [".map"] = "application/json; charset=utf-8"
        };

    private readonly List<string> _roots = new();

    /// <summary>Web roots searched in order. Only files beneath one of these are ever served.</summary>
    public IReadOnlyList<string> Roots => _roots;

    /// <summary>Registers a directory as a web root, ignoring paths that do not exist.</summary>
    public StaticContentHost AddRoot(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return this;

        try
        {
            string full = Path.GetFullPath(directory);
            if (Directory.Exists(full) && !_roots.Contains(full, StringComparer.OrdinalIgnoreCase))
            {
                _roots.Add(full);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // An unusable root is simply not registered.
        }

        return this;
    }

    /// <summary>
    /// Resolves a URL path to a file inside a registered root.
    /// Returns null when the request escapes every root or matches nothing.
    /// </summary>
    public string? Resolve(string urlPath)
    {
        string? relative = Normalize(urlPath);
        if (relative is null) return null;

        foreach (string root in _roots)
        {
            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(root, relative));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            // Canonical containment check: the resolved path must still sit under the root.
            if (!IsContainedIn(candidate, root)) continue;
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    public static string ContentTypeFor(string path) =>
        ContentTypes.TryGetValue(Path.GetExtension(path), out string? type)
            ? type
            : "application/octet-stream";

    /// <summary>Strips the leading slash, decodes, and rejects anything with traversal segments.</summary>
    private static string? Normalize(string urlPath)
    {
        if (string.IsNullOrWhiteSpace(urlPath)) return null;

        string decoded = Uri.UnescapeDataString(urlPath).Replace('\\', '/').TrimStart('/');
        if (decoded.Length == 0) return null;

        // Reject rooted paths and drive qualifiers outright; they can never be relative content.
        if (Path.IsPathRooted(decoded) || decoded.Contains(':')) return null;

        foreach (string segment in decoded.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..") return null;
        }

        return decoded;
    }

    private static bool IsContainedIn(string candidate, string root)
    {
        string normalizedRoot = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
