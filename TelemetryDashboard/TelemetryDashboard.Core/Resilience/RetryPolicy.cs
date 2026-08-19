using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace TelemetryDashboard.Core.Resilience;

/// <summary>
/// Retries a transient operation with exponential backoff.
/// </summary>
/// <remarks>
/// Every outbound integration in this codebase — Notion, Slack, MQTT, the update feed — talks to a
/// service that rate-limits or drops connections. Without a shared policy each one grows its own
/// ad-hoc loop, or more often none at all, and a single HTTP 429 silently discards an incident
/// report. The delay is injectable so retry behaviour can be tested without spending real seconds.
///
/// Backoff is deterministic doubling with no jitter. That is the right default for one desktop
/// host talking to a handful of services; a deployment fanning many nodes at one endpoint should
/// add jitter at the call site, because synchronised retries are how a rate limit becomes an
/// outage.
/// </remarks>
public static class RetryPolicy
{
    /// <summary>Delay before the first retry. Each subsequent wait doubles it.</summary>
    public static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Runs <paramref name="operation"/>, retrying while <paramref name="shouldRetry"/> accepts the
    /// failure. The exception from the final attempt propagates — exhausting the retries is a
    /// failure, not a silent null.
    /// </summary>
    /// <param name="delayAsync">
    /// Injected wait. Defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>; tests pass
    /// a no-op so they assert the retry count rather than the wall clock.
    /// </param>
    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        Func<Exception, bool>? shouldRetry = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "At least one attempt is required.");
        }

        Func<Exception, bool> retryable = shouldRetry ?? IsTransient;
        Func<TimeSpan, CancellationToken, Task> wait = delayAsync ?? Task.Delay;
        TimeSpan backoff = initialDelay ?? DefaultInitialDelay;

        for (int attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < maxAttempts && retryable(ex))
            {
                await wait(backoff, cancellationToken).ConfigureAwait(false);

                // Guard the doubling: a caller-supplied delay near TimeSpan.MaxValue would overflow.
                backoff = backoff < TimeSpan.FromHours(1) ? backoff * 2 : backoff;
            }
        }
    }

    /// <summary>Overload for operations that produce no value.</summary>
    public static Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        Func<Exception, bool>? shouldRetry = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return ExecuteAsync<bool>(
            async token =>
            {
                await operation(token).ConfigureAwait(false);
                return true;
            },
            maxAttempts,
            initialDelay,
            shouldRetry,
            delayAsync,
            cancellationToken);
    }

    /// <summary>
    /// Default classification of retryable failures: the network refused, timed out or broke.
    /// </summary>
    /// <remarks>
    /// <see cref="OperationCanceledException"/> is deliberately excluded. It normally means the
    /// caller asked to stop, and retrying past a cancellation ignores that instruction.
    /// </remarks>
    public static bool IsTransient(Exception exception) => exception switch
    {
        OperationCanceledException => false,
        HttpRequestException => true,
        SocketException => true,
        TimeoutException => true,
        IOException => true,
        _ => false
    };
}
