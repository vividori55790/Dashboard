using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Asking the console, over HTTP, whether it is really there.
/// </summary>
/// <remarks>
/// Split from the composition half because the two answer different questions: that one puts a
/// listener together, and this one declines to take its word for it.
/// </remarks>
public sealed partial class WebConsoleHost
{
    /// <summary>
    /// Reaches the running server over its own HTTP surface, to prove it answers before the
    /// banner claims it does.
    /// </summary>
    /// <remarks>
    /// This used to return the endpoint list parsed out of <c>/api/status</c>, on the reasoning
    /// that a list compiled into this host would keep printing an endpoint the day the server
    /// stopped serving it. Sound, and it stopped being possible: with <c>--credential</c> set the
    /// host holds a PBKDF2 derivation and not the password, so it cannot authenticate to itself,
    /// and the request came back 401. The banner read that as <em>unavailable -- /api/status did
    /// not answer</em> and printed it under a listener that had answered correctly and instantly.
    /// A start-up summary asserting a working component is dead is the same defect as one
    /// asserting a dead component works.
    /// <para>
    /// So the round trip stays and its meaning narrows to the half it can still establish: did
    /// something answer. 401 answers that as well as 200 does -- better, since it also proves the
    /// gate is in the path. The list itself now comes from
    /// <see cref="TelemetryStreamingServer.AdvertisedEndpoints"/>, which is the same object
    /// <c>/api/status</c> serialises, so the two cannot drift apart the way a second copy would.
    /// </para>
    /// </remarks>
    public async Task<ConsoleReachedResult> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using HttpResponseMessage response = await client
                .GetAsync($"{BaseAddress}/api/status", cancellationToken).ConfigureAwait(false);

            return response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? ConsoleReachedResult.AnsweredAndDemandedCredential
                : ConsoleReachedResult.Answered;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ConsoleReachedResult.NoAnswer;
        }
    }
}
