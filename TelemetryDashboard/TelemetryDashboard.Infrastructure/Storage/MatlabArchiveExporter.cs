using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>
/// Exports a window of the durable telemetry archive to a MATLAB MAT-file.
/// </summary>
/// <remarks>
/// This is the production path to <see cref="MatFileWriter"/>. The writer produced genuinely
/// loadable Level 4 files but nothing in the product ever called it, so the capability existed only
/// as a class: an engineer who wanted a recording in MATLAB, Octave or SciPy had no way to ask for
/// one.
/// <para>
/// The source is <see cref="IDataLogger"/> rather than <see cref="SqliteDataLogger"/> on purpose.
/// Export is a read over whatever store the host configured, and binding it to the SQLite type
/// would make the feature unavailable to any other implementation — including the in-memory
/// loggers used in tests, which is what lets this path be verified end to end.
/// </para>
/// </remarks>
public sealed class MatlabArchiveExporter : ITelemetryArchiveExporter
{
    private readonly IDataLogger _archive;
    private readonly MatFileWriter _writer = new();

    /// <summary>Creates an exporter reading from <paramref name="archive"/>.</summary>
    /// <param name="archive">Store the exported packets are queried from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="archive"/> is null.</exception>
    public MatlabArchiveExporter(IDataLogger archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        _archive = archive;
    }

    /// <summary>Extension of the files this exporter writes.</summary>
    /// <remarks>
    /// A constant as well as the interface property, so a caller deciding whether a destination
    /// path is one this exporter can write does not have to construct an exporter -- and therefore
    /// does not have to invent an archive to construct it against -- to ask.
    /// </remarks>
    public const string Extension = ".mat";

    /// <inheritdoc />
    public string FileExtension => Extension;

    /// <summary>
    /// Queries the archive and writes one MAT matrix per channel, returning the packet count.
    /// </summary>
    /// <remarks>
    /// The query is materialised before the file is opened. <see cref="MatFileWriter"/> needs each
    /// channel's full row count in the matrix header it writes ahead of that channel's data, so it
    /// cannot stream; doing the read first also means a failing query leaves no truncated file
    /// behind for someone to load and mistake for a short recording.
    /// <para>
    /// Cancellation is honoured during the query only. Once the file is being written the export is
    /// carried through: abandoning a MAT-file part way leaves a matrix header promising rows that
    /// are not there, which loads as corrupt rather than as incomplete.
    /// </para>
    /// <para>
    /// A filter that matches nothing produces no file, and removes one already at the destination.
    /// A Level 4 MAT-file holding no matrices is zero bytes, and a zero-byte file is not loadable —
    /// SciPy reports it as truncated — so writing one would hand the operator a broken file for what
    /// is really an empty result. Deleting instead of leaving the path untouched matters just as
    /// much: an earlier export sitting there would be opened as though it were this one.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="targetFilePath"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="filter"/> is null.</exception>
    /// <exception cref="DirectoryNotFoundException">The destination folder is absent.</exception>
    public async Task<int> ExportAsync(
        string targetFilePath,
        QueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetFilePath))
        {
            throw new ArgumentException("Target path must be provided.", nameof(targetFilePath));
        }

        ArgumentNullException.ThrowIfNull(filter);

        IEnumerable<TelemetryPacket> matched =
            await _archive.QueryAsync(filter, cancellationToken).ConfigureAwait(false);

        List<TelemetryPacket> packets = matched as List<TelemetryPacket> ?? matched.ToList();

        if (packets.Count == 0)
        {
            File.Delete(targetFilePath);
            return 0;
        }

        _writer.WritePackets(targetFilePath, packets);
        return packets.Count;
    }
}
