namespace TelemetryDashboard.Core.Interfaces;

/// <summary>
/// Writes a span of recorded telemetry — the span an <see cref="IDataLogger"/> returns for a
/// <see cref="QueryFilter"/> — into a file that an external analysis tool can open.
/// </summary>
/// <remarks>
/// The contract sits in Core, beside <see cref="IDataLogger"/>, because the callers who want an
/// export are the ones who already hold a logger: the desktop shell, a headless host, a plugin
/// reaching the host through <c>IPluginContext</c>. Putting the entry point only in the adapter
/// layer would force every one of them to compile against a storage adapter to offer a menu item,
/// which is the coupling <c>Core_DoesNotDependOnInfrastructureOrUi</c> exists to prevent.
/// <para>
/// The file format is deliberately absent from the contract. The caller picks an exporter, not a
/// serialiser; formats that need a binary writer, a native library or a platform API can then live
/// in Infrastructure without dragging any of that into the domain layer.
/// </para>
/// </remarks>
public interface ITelemetryArchiveExporter
{
    /// <summary>Extension the exporter's files carry, including the leading dot.</summary>
    /// <remarks>
    /// Exposed so a caller can build a file-dialog filter or a default file name without knowing
    /// which exporter it was handed — the one detail of the format a caller legitimately needs.
    /// </remarks>
    string FileExtension { get; }

    /// <summary>
    /// Exports every packet matching <paramref name="filter"/> to
    /// <paramref name="targetFilePath"/> and returns how many packets were written.
    /// </summary>
    /// <remarks>
    /// The count is the caller's signal that the filter matched anything, and implementations are
    /// free to write no file at all for an empty result — a caller that reports success on the
    /// absence of an exception will describe an empty export as a finished one. Note also that
    /// <see cref="QueryFilter.Limit"/> defaults to 1000, so an unqualified filter exports the oldest
    /// 1000 packets and silently leaves the rest of the archive behind.
    /// </remarks>
    /// <param name="targetFilePath">Destination file. Any file already there is replaced.</param>
    /// <param name="filter">Selects the packets to export.</param>
    /// <param name="cancellationToken">Cancels the query before the file is written.</param>
    Task<int> ExportAsync(
        string targetFilePath,
        QueryFilter filter,
        CancellationToken cancellationToken = default);
}
