using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Infrastructure.WebServer;

/// <summary>
/// Posts Block Kit alerts to a Slack incoming webhook.
/// </summary>
/// <remarks>
/// Delivery failures are reported as <c>false</c> rather than thrown: an alert channel that
/// takes down the caller when Slack is unreachable defeats its own purpose. The alert itself is
/// still recorded locally by the caller regardless of the transport result.
/// </remarks>
public sealed class SlackClient : ISlackClient
{
    private static readonly JsonSerializerOptions BlockKitJsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly HttpClient _http;

    /// <summary>Attempts per alert, including the first.</summary>
    /// <remarks>
    /// Slack answers a burst with <c>429</c> and a <c>Retry-After</c> header. That is a response,
    /// not an exception, so without status-aware retry an alert raised during exactly the incident
    /// that generates a burst is the one most likely to be dropped.
    /// </remarks>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>Injected wait between attempts; tests substitute a no-op to assert retries.</summary>
    public Func<TimeSpan, CancellationToken, Task>? RetryDelay { get; init; }

    public SlackClient(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public Task<bool> SendAlertAsync(string webhookUrl, string message) =>
        SendAlertAsync(webhookUrl, message, CancellationToken.None);

    public async Task<bool> SendAlertAsync(string webhookUrl, string message, CancellationToken cancellationToken)
    {
        // Reject locally what Slack would reject anyway, without spending a round trip.
        if (string.IsNullOrWhiteSpace(webhookUrl)) return false;
        if (string.IsNullOrWhiteSpace(message)) return false;
        if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out Uri? uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;
        if (!HasUsableWebhookPath(uri)) return false;

        try
        {
            // Content is rebuilt per attempt: a StringContent is consumed by the first send, so a
            // retry reusing it would throw rather than retry.
            using HttpResponseMessage response = await HttpRetryExecutor.SendAsync(
                token => _http.PostAsync(
                    uri,
                    new StringContent(FormatBlockKitJson(message), Encoding.UTF8, "application/json"),
                    token),
                MaxAttempts,
                delayAsync: RetryDelay,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when the path could plausibly address a real webhook.
    /// </summary>
    /// <remarks>
    /// Only enforced for <c>hooks.slack.com</c>, where the shape is documented and stable
    /// (<c>/services/{team}/{bot}/{token}</c>). A mistyped webhook otherwise posts alerts into a
    /// 404 forever and the operator sees "sent" with nothing arriving. Any other host is left
    /// alone so a relay, proxy or test double still works.
    /// </remarks>
    private static bool HasUsableWebhookPath(Uri uri)
    {
        if (!uri.Host.Equals("hooks.slack.com", StringComparison.OrdinalIgnoreCase)) return true;

        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Length == 4
               && segments[0].Equals("services", StringComparison.OrdinalIgnoreCase)
               && segments[3].Length >= MinimumWebhookTokenLength;
    }

    /// <summary>Slack issues 24-character webhook tokens; anything far shorter is a typo.</summary>
    private const int MinimumWebhookTokenLength = 16;

    /// <summary>
    /// Builds the Block Kit payload. All interpolation goes through the JSON serializer, so
    /// quotes and braces in an alert message cannot break out of the payload.
    /// </summary>
    /// <remarks>
    /// Uses relaxed escaping so a quote is emitted as a backslash-quote pair rather than the
    /// numeric u0022 escape the strict encoder produces. Both are valid JSON and decode
    /// identically, but the readable form is what an operator sees when
    /// an alert payload is dumped into a log while diagnosing a failed delivery. The payload is a
    /// webhook body and is never embedded in HTML, so the HTML-escaping the strict encoder adds
    /// buys nothing here.
    /// </remarks>
    public string FormatBlockKitJson(string message)
    {
        var payload = new
        {
            text = message,
            blocks = new object[]
            {
                new
                {
                    type = "header",
                    text = new { type = "plain_text", text = "TelemetryDashboard Alert", emoji = true }
                },
                new
                {
                    type = "section",
                    text = new { type = "mrkdwn", text = message }
                },
                new
                {
                    type = "context",
                    elements = new object[]
                    {
                        new { type = "mrkdwn", text = $"Sent {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC" }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(payload, BlockKitJsonOptions);
    }
}
