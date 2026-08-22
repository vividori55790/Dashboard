using System;
using System.IO;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Host.Archive;

/// <summary>
/// Turning what an operator typed after <c>--out</c> into the file that will be written.
/// </summary>
/// <remarks>
/// Both decisions here are about being wrong early rather than late. A destination whose folder is
/// absent, or whose extension names a format nothing can write, fails inside the exporter after the
/// archive has been read — and the message that surfaces is the framework's, about a path, rather
/// than one about the flag that carried it.
/// </remarks>
public static class ExportDestination
{
    /// <summary>
    /// Resolves <paramref name="outputPath"/> to a writable target, or says why it is not one.
    /// </summary>
    /// <remarks>
    /// A path with no extension gets the exporter's, because <c>--out bench</c> means a file called
    /// bench and there is only one kind this can write. A path carrying a <em>different</em>
    /// extension is refused rather than corrected: somebody who wrote <c>--out bench.csv</c> asked
    /// for a CSV, and handing them a MAT-file named .csv would be answering a different question
    /// under the name of theirs.
    /// </remarks>
    public static bool TryResolve(string outputPath, out string target, out string? refusal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        target = string.Empty;
        refusal = null;

        if (Path.GetExtension(outputPath) is { Length: > 0 } extension
            && !string.Equals(extension, MatlabArchiveExporter.Extension, StringComparison.OrdinalIgnoreCase))
        {
            refusal = $"--out writes {MatlabArchiveExporter.Extension} files, and '{extension}' is not one.";
            return false;
        }

        string resolved = Path.HasExtension(outputPath)
            ? outputPath
            : outputPath + MatlabArchiveExporter.Extension;

        if (Path.GetDirectoryName(resolved) is { Length: > 0 } folder && !Directory.Exists(folder))
        {
            refusal = $"the folder '{folder}' does not exist, so nothing can be written there.";
            return false;
        }

        target = resolved;
        return true;
    }
}
