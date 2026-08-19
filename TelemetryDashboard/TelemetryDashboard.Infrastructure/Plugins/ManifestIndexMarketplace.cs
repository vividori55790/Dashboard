using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// Extension catalogue backed by a JSON index — a file on disk, a network share, or a URL.
/// </summary>
/// <remarks>
/// <see cref="IMarketplaceService"/> was declared with no implementation anywhere, so the
/// marketplace was a feature the codebase claimed and did not have.
///
/// An index file rather than a bespoke service is the whole point: a team hosts one JSON document
/// wherever it already hosts anything, and a machine on an isolated plant network points at a
/// local path instead. Requiring a server to install an extension would have put this feature out
/// of reach of exactly the deployments the hub is built for.
/// </remarks>
public sealed class ManifestIndexMarketplace : IMarketplaceService
{
    private readonly string _indexLocation;
    private readonly HttpClient _http;
    private readonly PluginManifestParser _parser = new();

    /// <param name="indexLocation">
    /// An <c>http(s)</c> URL or a filesystem path to a JSON array of extension manifests.
    /// </param>
    public ManifestIndexMarketplace(string indexLocation, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(indexLocation))
        {
            throw new ArgumentException("A catalogue location must be provided.", nameof(indexLocation));
        }

        _indexLocation = indexLocation.Trim();
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>Entries the last fetch rejected as unparseable.</summary>
    /// <remarks>
    /// Surfaced rather than logged and forgotten: a catalogue that silently lists nine of ten
    /// extensions looks complete, and the missing one is indistinguishable from one that was never
    /// published.
    /// </remarks>
    public int LastRejectedCount { get; private set; }

    /// <inheritdoc />
    public async Task<List<ExtensionDescriptor>> FetchAvailableExtensionsAsync(CancellationToken cancellationToken = default)
    {
        string json = await ReadIndexAsync(cancellationToken).ConfigureAwait(false);
        return ParseIndex(json);
    }

    /// <summary>Reads the raw index. Transport failures propagate so the caller can show an offline state.</summary>
    private async Task<string> ReadIndexAsync(CancellationToken cancellationToken)
    {
        if (_indexLocation.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || _indexLocation.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            using HttpResponseMessage response = await _http.GetAsync(_indexLocation, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!File.Exists(_indexLocation))
        {
            throw new FileNotFoundException($"Extension catalogue not found: {_indexLocation}", _indexLocation);
        }

        return await File.ReadAllTextAsync(_indexLocation, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Splits the index into entries and parses each independently.
    /// </summary>
    /// <remarks>
    /// One malformed manifest must not discard the rest of the catalogue — a third party publishes
    /// these, and a single bad entry taking down the whole listing hands any publisher the ability
    /// to break everyone else's. Rejected entries are counted in <see cref="LastRejectedCount"/>.
    /// </remarks>
    private List<ExtensionDescriptor> ParseIndex(string json)
    {
        var descriptors = new List<ExtensionDescriptor>();
        LastRejectedCount = 0;

        foreach (string entry in ManifestIndexSplitter.SplitTopLevelObjects(json))
        {
            if (_parser.TryParseManifest(entry, out ExtensionDescriptor? descriptor) && descriptor is not null)
            {
                descriptors.Add(descriptor);
            }
            else
            {
                LastRejectedCount++;
            }
        }

        return descriptors;
    }
}
