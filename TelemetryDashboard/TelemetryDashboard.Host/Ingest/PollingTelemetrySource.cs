using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// Polls an HTTP endpoint on a fixed interval and feeds each response into the ingest path.
/// </summary>
/// <remarks>
/// Most public real-time data is not a stream. Korea's open-data portals, USGS, most exchange REST
/// APIs and nearly every industrial gateway answer a request and hang up; a hub that can only
/// consume Server-Sent Events cannot read any of them. This is the other half.
///
/// It is honest about what polling is. A poll is a sample of whatever the endpoint said at that
/// instant, not a record of everything that happened between polls, and events shorter than the
/// interval are simply not observed. That is a property of the method rather than a defect, but it
/// has to be visible: <see cref="PollsAttempted"/> and <see cref="PollsFailed"/> let an operator
/// see that a flat chart is a flat signal and not a dead poller.
///
/// The interval has a floor. A misconfigured host hammering a free public endpoint is how open
/// infrastructure gets closed, and a typo in a config file should not be able to do that.
/// </remarks>
public sealed class PollingTelemetrySource : ITelemetrySource
{
    /// <summary>Fastest permitted poll. Below this the caller is denying service, not measuring.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(250);

    private readonly Uri _endpoint;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public PollingTelemetrySource(string endpoint, TimeSpan interval, HttpClient? httpClient = null)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"'{endpoint}' is not an absolute http(s) URL.", nameof(endpoint));
        }

        if (interval < MinimumInterval)
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                $"A poll interval below {MinimumInterval.TotalMilliseconds:N0} ms would hammer the endpoint "
                + "rather than measure it. Public feeds are shared, and a typo should not be able to abuse one.");
        }

        _endpoint = uri;
        Interval = interval;
        _ownsClient = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd(SseTelemetrySource.UserAgent))
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", SseTelemetrySource.UserAgent);
        }
    }

    /// <summary>How often the endpoint is asked.</summary>
    public TimeSpan Interval { get; }

    /// <inheritdoc />
    public string Origin => "NETWORK_POLL";

    /// <summary>Measured data from somewhere else, not synthesised here.</summary>
    public bool IsSimulated => false;

    /// <inheritdoc />
    public string Description => $"poll {_endpoint} every {Interval.TotalSeconds:0.##}s";

    /// <summary>Requests made, successful or not.</summary>
    public long PollsAttempted { get; private set; }

    /// <summary>Requests that did not return a usable body.</summary>
    /// <remarks>
    /// Counted separately from attempts because the two failures look identical on a chart. A
    /// signal that stopped changing and a poller that stopped succeeding both draw a flat line.
    /// </remarks>
    public long PollsFailed { get; private set; }

    /// <summary>Why the last poll failed, or null when the last one succeeded.</summary>
    public string? LastFault { get; private set; }

    /// <inheritdoc />
    public async IAsyncEnumerable<RawPacket> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (!cancellationToken.IsCancellationRequested)
        {
            string? body = await PollAsync(cancellationToken).ConfigureAwait(false);

            if (body is not null)
            {
                yield return new RawPacket(_endpoint.Host, body, DateTime.UtcNow);
            }

            bool alive;
            try
            {
                alive = await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            if (!alive) yield break;
        }
    }

    /// <summary>
    /// One request. Never throws: a failed poll is a gap, and a gap must not end the run.
    /// </summary>
    /// <remarks>
    /// The endpoint being briefly unavailable is ordinary. Taking the whole host down for it would
    /// mean a transient outage on one feed loses the recording of every other source, which is a
    /// far worse outcome than a missing sample.
    /// </remarks>
    private async Task<string?> PollAsync(CancellationToken cancellationToken)
    {
        PollsAttempted++;

        try
        {
            using HttpResponseMessage response = await _http
                .GetAsync(_endpoint, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                PollsFailed++;
                Report($"HTTP {(int)response.StatusCode}");
                return null;
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            LastFault = null;
            return body;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.IO.IOException)
        {
            PollsFailed++;
            Report($"{ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private void Report(string fault)
    {
        // Only on a change of state, so a long outage does not bury everything else in the log
        // while still being announced when it starts.
        if (LastFault == fault) return;

        LastFault = fault;
        Console.Error.WriteLine(
            $"[poll] {_endpoint.Host} failed: {fault} ({PollsFailed:N0} of {PollsAttempted:N0} polls). "
            + "Samples are missing for this interval, not flat.");
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_ownsClient) _http.Dispose();
        return ValueTask.CompletedTask;
    }
}
