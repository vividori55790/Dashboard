using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TelemetryDashboard.Infrastructure.Analytics;

/// <summary>
/// Scores a window by POSTing it to a model service and reading the answer back.
/// </summary>
/// <remarks>
/// The interesting half of this class is what it does when the service misbehaves, because that is
/// what it will spend most of its life doing. Every failure — refused connection, DNS, a 500, a
/// body that is not JSON, JSON with no score, a score that is NaN — produces null and a counter,
/// never a number. Nothing here has a fallback value, because a fallback anomaly score is a
/// fabricated one. The timeout is enforced with a linked token rather than by trusting
/// <see cref="HttpClient.Timeout"/> alone, so it still holds when a caller supplies its own handler.
/// </remarks>
public sealed class HttpInferenceEndpoint : IInferenceEndpoint, IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly TimeSpan _timeout;

    /// <param name="endpoint">URL the window is POSTed to.</param>
    /// <param name="timeout">How long one request may take before it is abandoned and counted.</param>
    /// <param name="modelId">Model identity carried into the request and the detector id.</param>
    /// <param name="handler">Transport, for tests. When supplied, the caller owns it, not this type.</param>
    public HttpInferenceEndpoint(Uri endpoint, TimeSpan timeout, string modelId = "model", HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout), "A request with no time limit can hold a queue open indefinitely.");

        _endpoint = endpoint;
        _timeout = timeout;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);

        // Slack, not a second deadline: the linked token below is the one that decides. Setting
        // them equal makes which exception surfaces a race, and the two are counted differently.
        _http.Timeout = timeout + TimeSpan.FromSeconds(5);

        ModelId = string.IsNullOrWhiteSpace(modelId) ? "model" : modelId.Trim();
        EndpointId = $"http:{endpoint.Host}:{endpoint.Port}/{ModelId}";
    }

    /// <summary>Model identity sent with every request.</summary>
    public string ModelId { get; }

    /// <inheritdoc />
    public string EndpointId { get; }

    /// <summary>What this endpoint delivered, and how it failed.</summary>
    public InferenceTally Tally { get; } = new();

    /// <inheritdoc />
    public async Task<InferenceScore?> ScoreAsync(InferenceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);

        try
        {
            using var content = new StringContent(Serialise(request), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _http
                .PostAsync(_endpoint, content, deadline.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Tally.CountRefused();
                return null;
            }

            string body = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
            return Read(body, request);
        }
        // Shutdown is rethrown; a timeout is counted. Conflating them would report a clean stop as
        // a model failure, and a model failure as a clean stop.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            Tally.CountTimedOut();
            return null;
        }
        catch (Exception)
        {
            // HttpRequestException, socket faults, TLS failures: the endpoint did not answer.
            Tally.CountRefused();
            return null;
        }
    }

    private static string Serialise(InferenceRequest request) =>
        JsonSerializer.Serialize(new
        {
            channel = request.Channel,
            modelId = request.ModelId,
            windowEndUtc = request.WindowEndUtc.ToString("o"),
            samples = request.Window
        });

    /// <summary>Reads a score out of a response body, or returns null when there is not one.</summary>
    /// <remarks>
    /// Strict on purpose. A response missing the score field, or carrying a NaN, is a broken model
    /// server — and the tempting reading of "no score field means nothing was wrong" is exactly the
    /// inference this codebase forbids.
    /// </remarks>
    private InferenceScore? Read(string body, InferenceRequest request)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("score", out JsonElement scoreElement)
                || scoreElement.ValueKind != JsonValueKind.Number
                || !scoreElement.TryGetDouble(out double score)
                || !double.IsFinite(score))
            {
                Tally.CountUnusable();
                return null;
            }

            bool? judgement = root.TryGetProperty("isAnomaly", out JsonElement flag)
                && flag.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? flag.GetBoolean()
                    : null;

            string? reportedModel = root.TryGetProperty("modelId", out JsonElement id)
                && id.ValueKind == JsonValueKind.String ? id.GetString() : null;

            Tally.CountAccepted();
            return new InferenceScore(score, judgement, reportedModel, request.WindowEndUtc, DateTime.UtcNow);
        }
        catch (JsonException)
        {
            Tally.CountUnusable();
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
