using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// Reads Server-Sent Events from an HTTP endpoint and feeds each event into the ingest path.
/// </summary>
/// <remarks>
/// A serial cable is not the only thing that produces a stream of measurements, and a hub that can
/// only read one is a hub tied to one building. This source is the network equivalent: point it at
/// any SSE endpoint and each event becomes a frame, parsed by the same routing rules, scored by the
/// same analytics and recorded to the same archive as a device on a port.
///
/// Deliberately generic. Naming a class after one provider is how this codebase ended up welded to
/// a single power-converter example; what varies between feeds is a URL and a channel map, and both
/// of those are configuration.
///
/// It reconnects, and it counts reconnections. A feed that drops every thirty seconds and silently
/// resumes looks identical to a healthy one from the chart, and the gaps it leaves are exactly the
/// intervals an operator would otherwise read as quiet.
/// </remarks>
public sealed class SseTelemetrySource : ITelemetrySource
{
    private readonly Uri _endpoint;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public SseTelemetrySource(string endpoint, HttpClient? httpClient = null)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"'{endpoint}' is not an absolute http(s) URL.", nameof(endpoint));
        }

        _endpoint = uri;
        _ownsClient = httpClient is null;

        // No overall timeout: this request is meant to stay open indefinitely, and the default
        // hundred seconds would tear down a perfectly healthy stream on a fixed schedule.
        _http = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        // Several public feeds refuse an anonymous client outright -- Wikimedia answers 403 to a
        // request with no User-Agent, which is their stated policy rather than a fault. Identifying
        // the software and its purpose is the courtesy that keeps open infrastructure open.
        if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd(UserAgent))
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        }
    }

    /// <summary>Identifies this software to the feed, as open infrastructure asks callers to do.</summary>
    public const string UserAgent = "TelemetryDashboard/2.0 (telemetry ingest; https://github.com/vividori55790)";

    /// <inheritdoc />
    public string Origin => "NETWORK_STREAM";

    /// <summary>Measured data from somewhere else, not synthesised here.</summary>
    public bool IsSimulated => false;

    /// <inheritdoc />
    public string Description => $"SSE {_endpoint}";

    /// <summary>How many times the connection dropped and was re-established.</summary>
    public int Reconnects { get; private set; }

    /// <summary>Events received across all connections.</summary>
    public long EventsReceived { get; private set; }

    /// <summary>Why the last connection ended, or null while it is healthy.</summary>
    public string? LastFault { get; private set; }

    /// <summary>Wait before reconnecting. Fixed and short: the endpoint is not ours to hammer.</summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(3);

    /// <inheritdoc />
    public async IAsyncEnumerable<RawPacket> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            IAsyncEnumerator<string>? events = null;

            try
            {
                events = ReadEventsAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                LastFault = $"{ex.GetType().Name}: {ex.Message}";
            }

            if (events is not null)
            {
                while (true)
                {
                    string? payload;
                    try
                    {
                        if (!await events.MoveNextAsync().ConfigureAwait(false)) break;
                        payload = events.Current;
                    }
                    catch (OperationCanceledException)
                    {
                        await events.DisposeAsync().ConfigureAwait(false);
                        yield break;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or IOException)
                    {
                        LastFault = $"{ex.GetType().Name}: {ex.Message}";
                        break;
                    }

                    EventsReceived++;
                    yield return new RawPacket(_endpoint.Host, payload, DateTime.UtcNow);
                }

                await events.DisposeAsync().ConfigureAwait(false);
            }

            if (cancellationToken.IsCancellationRequested) yield break;

            Reconnects++;
            Console.Error.WriteLine(
                $"[sse] {_endpoint.Host} disconnected after {EventsReceived:N0} events"
                + (LastFault is null ? "." : $": {LastFault}")
                + $" Reconnecting in {ReconnectDelay.TotalSeconds:0.#}s (attempt {Reconnects}).");

            try
            {
                await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    /// <summary>Streams one payload per SSE event, joining multi-line <c>data:</c> fields.</summary>
    private async IAsyncEnumerable<string> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
        request.Headers.Accept.ParseAdd("text/event-stream");

        using HttpResponseMessage response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        LastFault = null;

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var data = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) yield break;

            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    yield return data.ToString();
                    data.Clear();
                }
                continue;
            }

            // Comments (":ok" keep-alives) and the id/event fields carry no measurement.
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line.AsSpan(5).TrimStart());
            }
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_ownsClient) _http.Dispose();
        return ValueTask.CompletedTask;
    }
}
