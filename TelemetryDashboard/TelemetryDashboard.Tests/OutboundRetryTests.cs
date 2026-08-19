using System.Net;
using System.Net.Http;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Updater;
using TelemetryDashboard.Infrastructure.WebServer;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Proves the outbound integrations actually retry a rate limit.
/// </summary>
/// <remarks>
/// A shared <c>RetryPolicy</c> existed but nothing used it, and it would not have helped anyway:
/// it classifies <em>exceptions</em>, and HTTP 429 arrives as a successful response. So a
/// rate-limited incident report was dropped while the caller was told the transport worked. These
/// tests drive the real clients through a fake transport and count the attempts.
/// </remarks>
public class OutboundRetryTests
{
    /// <summary>Replays a scripted sequence of responses and records how many times it was called.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public ScriptedHandler(params HttpResponseMessage[] responses) => _responses = new Queue<HttpResponseMessage>(responses);

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    private static HttpResponseMessage Status(HttpStatusCode code, string body = "{}") =>
        new(code) { Content = new StringContent(body) };

    /// <summary>No-op wait, so these assert retry counts rather than elapsed seconds.</summary>
    private static readonly Func<TimeSpan, CancellationToken, Task> NoWait = (_, _) => Task.CompletedTask;

    [Fact]
    public async Task RateLimit_IsRetried_BecauseItArrivesAsAResponseNotAnException()
    {
        var handler = new ScriptedHandler(
            Status(HttpStatusCode.TooManyRequests),
            Status(HttpStatusCode.OK));

        using HttpResponseMessage response = await HttpRetryExecutor.SendAsync(
            token => new HttpClient(handler).GetAsync("https://example.invalid/x", token),
            maxAttempts: 3,
            delayAsync: NoWait);

        handler.Calls.Should().Be(2);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RetryAfterHeader_IsObeyedInsteadOfOurOwnBackoff()
    {
        HttpResponseMessage limited = Status(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));

        var handler = new ScriptedHandler(limited, Status(HttpStatusCode.OK));
        var waits = new List<TimeSpan>();

        using HttpResponseMessage response = await HttpRetryExecutor.SendAsync(
            token => new HttpClient(handler).GetAsync("https://example.invalid/x", token),
            maxAttempts: 3,
            initialDelay: TimeSpan.FromMilliseconds(50),
            delayAsync: (delay, _) => { waits.Add(delay); return Task.CompletedTask; });

        // Backing off on our own schedule while the server stated its own is how a rate limit
        // becomes a longer rate limit.
        waits.Should().ContainSingle().Which.Should().Be(TimeSpan.FromSeconds(7));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ClientError_IsNotRetried_BecauseItFailsIdenticallyEveryTime()
    {
        var handler = new ScriptedHandler(Status(HttpStatusCode.BadRequest));

        using HttpResponseMessage response = await HttpRetryExecutor.SendAsync(
            token => new HttpClient(handler).GetAsync("https://example.invalid/x", token),
            maxAttempts: 4,
            delayAsync: NoWait);

        handler.Calls.Should().Be(1, "retrying a malformed request only delays the error");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task NotionClient_RetriesARateLimitedPublish_AndReturnsThePageId()
    {
        var handler = new ScriptedHandler(
            Status(HttpStatusCode.TooManyRequests),
            Status(HttpStatusCode.OK, "{\"id\":\"PAGE_ID_123\"}"));

        var client = new NotionClient(
            "secret_" + new string('a', 40),
            new HttpClient(handler))
        {
            MaxAttempts = 3,
            RetryDelay = NoWait
        };

        string pageId = await client.CreateReportPageAsync(
            new string('a', 32), "Incident", new List<TelemetryPacket>());

        handler.Calls.Should().Be(2);
        pageId.Should().Be("PAGE_ID_123");
    }

    [Fact]
    public async Task SlackClient_RetriesARateLimitedAlert()
    {
        var handler = new ScriptedHandler(
            Status(HttpStatusCode.TooManyRequests),
            Status(HttpStatusCode.OK, "ok"));

        var slack = new SlackClient(new HttpClient(handler)) { MaxAttempts = 3, RetryDelay = NoWait };

        bool sent = await slack.SendAlertAsync(
            "https://hooks.slack.com/services/T00000000/B00000000/" + new string('x', 24),
            "thermal excursion");

        handler.Calls.Should().Be(2);
        sent.Should().BeTrue();
    }

    [Fact]
    public async Task GitHubUpdater_RetriesARateLimitedFeedQuery()
    {
        var handler = new ScriptedHandler(
            Status(HttpStatusCode.TooManyRequests),
            Status(HttpStatusCode.OK, "{\"tag_name\":\"v2.0.0\",\"assets\":[]}"));

        var updater = new GitHubUpdater(new HttpClient(handler)) { MaxAttempts = 3, RetryDelay = NoWait };
        updater.SetCurrentVersion("1.0.0");

        UpdateCheckResult result = await updater.CheckForUpdatesAsync("owner/repo");

        handler.Calls.Should().Be(2);
        result.IsUpdateAvailable.Should().BeTrue();
        result.LatestVersion.Should().Be("v2.0.0");
    }
}
