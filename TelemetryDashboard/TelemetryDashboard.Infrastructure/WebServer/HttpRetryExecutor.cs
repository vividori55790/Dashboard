using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Resilience;

namespace TelemetryDashboard.Infrastructure.WebServer;

/// <summary>
/// Sends an HTTP request with retries, honouring both transport faults and retryable statuses.
/// </summary>
/// <remarks>
/// <see cref="RetryPolicy"/> alone is not enough for HTTP, and the gap is the exact case it was
/// written for. A rate limit arrives as a <c>429</c> <em>response</em>, not an exception, so a
/// policy that classifies exceptions sees a perfectly successful call and never retries — the
/// incident report is discarded and the caller is told the transport worked.
///
/// <c>Retry-After</c> is obeyed when the server sends it. Backing off on our own schedule while the
/// server has stated its own is how a rate limit turns into a longer rate limit.
/// </remarks>
public static class HttpRetryExecutor
{
    /// <summary>Statuses worth a second attempt: rate limiting and transient server faults.</summary>
    /// <remarks>
    /// 4xx other than 429 are excluded deliberately — a malformed request or a rejected credential
    /// fails identically on every attempt, and retrying only delays the error the caller needs.
    /// </remarks>
    public static bool IsRetryableStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.TooManyRequests => true,
        HttpStatusCode.RequestTimeout => true,
        HttpStatusCode.InternalServerError => true,
        HttpStatusCode.BadGateway => true,
        HttpStatusCode.ServiceUnavailable => true,
        HttpStatusCode.GatewayTimeout => true,
        _ => false
    };

    /// <summary>
    /// Runs <paramref name="send"/> until it yields a non-retryable outcome or the attempts run out.
    /// </summary>
    /// <param name="delayAsync">Injected wait; tests pass a no-op so they assert retries, not seconds.</param>
    public static async Task<HttpResponseMessage> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(send);

        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "At least one attempt is required.");
        }

        Func<TimeSpan, CancellationToken, Task> wait = delayAsync ?? Task.Delay;
        TimeSpan backoff = initialDelay ?? RetryPolicy.DefaultInitialDelay;

        for (int attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HttpResponseMessage response;
            try
            {
                response = await send(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < maxAttempts && RetryPolicy.IsTransient(ex))
            {
                await wait(backoff, cancellationToken).ConfigureAwait(false);
                backoff = Grow(backoff);
                continue;
            }

            if (attempt >= maxAttempts || !IsRetryableStatus(response.StatusCode))
            {
                return response;
            }

            TimeSpan pause = RetryAfter(response) ?? backoff;

            // The response is abandoned, so its connection must be released before the next attempt.
            response.Dispose();

            await wait(pause, cancellationToken).ConfigureAwait(false);
            backoff = Grow(backoff);
        }
    }

    /// <summary>The server's own <c>Retry-After</c>, as a delay, when it sent a usable one.</summary>
    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        System.Net.Http.Headers.RetryConditionHeaderValue? header = response.Headers.RetryAfter;
        if (header is null) return null;

        if (header.Delta is { } delta && delta > TimeSpan.Zero) return delta;

        if (header.Date is { } date)
        {
            TimeSpan until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero) return until;
        }

        return null;
    }

    /// <summary>Doubles the wait, with a ceiling so a caller-supplied value cannot overflow.</summary>
    private static TimeSpan Grow(TimeSpan current) =>
        current < TimeSpan.FromHours(1) ? current * 2 : current;
}
