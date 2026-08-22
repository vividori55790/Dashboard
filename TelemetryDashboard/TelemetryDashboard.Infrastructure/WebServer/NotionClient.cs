using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Infrastructure.WebServer;

/// <summary>
/// Publishes telemetry reports as pages in a Notion database.
/// </summary>
/// <remarks>
/// Credentials and identifiers are validated before any request leaves the machine, so a typo
/// produces an immediate, specific error instead of an opaque HTTP failure. A token shaped like a
/// real Notion secret (<c>secret_</c> or <c>ntn_</c>) must be well formed; anything else is
/// treated as an offline placeholder and no network call is attempted.
/// </remarks>
public sealed class NotionClient : INotionClient
{
    private const string ApiVersion = "2022-06-28";

    private readonly HttpClient _http;
    private readonly string _token;

    public NotionClient(string integrationToken, HttpClient? httpClient = null)
    {
        _token = integrationToken ?? string.Empty;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>True when the token claims to be a real Notion credential.</summary>
    public bool IsLiveToken => NotionCredentialValidator.IsLiveToken(_token);

    /// <summary>Attempts per publish, including the first.</summary>
    /// <remarks>
    /// Notion rate-limits at roughly three requests a second and answers <c>429</c>, which is a
    /// response rather than an exception — so before this the report was simply dropped and the
    /// caller was told the transport had worked.
    /// </remarks>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>Injected wait between attempts; tests substitute a no-op to assert retries.</summary>
    public Func<TimeSpan, CancellationToken, Task>? RetryDelay { get; init; }

    /// <summary>Creates a report page and returns its page id.</summary>
    /// <exception cref="UnauthorizedAccessException">The token is malformed or rejected.</exception>
    /// <exception cref="KeyNotFoundException">The database id is malformed or absent.</exception>
    public async Task<string> CreateReportPageAsync(string databaseId, string title, List<TelemetryPacket> packets) =>
        await CreateReportPageAsync(databaseId, title, packets, CancellationToken.None).ConfigureAwait(false);

    public async Task<string> CreateReportPageAsync(
        string databaseId, string title, List<TelemetryPacket> packets, CancellationToken cancellationToken)
    {
        // Shape checks first. They are local, cheap, and independent of whether anything will be
        // transmitted, so a caller gets the same diagnosis for a malformed argument either way.
        NotionCredentialValidator.ValidateToken(_token);
        NotionCredentialValidator.ValidateDatabaseId(databaseId);

        // Then the transmission gate the class remarks promise but never enforced: a token not
        // shaped like a Notion secret used to fall through to SendAsync, so a configured
        // placeholder such as "mock_key" produced a real outbound request to api.notion.com
        // carrying that string as a bearer credential. Refuse loudly — quietly doing nothing
        // would leave the operator believing the report was published.
        if (!IsLiveToken)
        {
            throw new UnauthorizedAccessException(
                "Notion token is not a live integration secret (expected a 'secret_' or 'ntn_' prefix), "
                + "so no request was sent. Use SaveLocalBackupPayload to keep the report offline.");
        }

        string payload = BuildPagePayload(databaseId, title, packets);

        // Built fresh per attempt: an HttpRequestMessage may only be sent once, so a retry that
        // reused it would fail with InvalidOperationException instead of retrying.
        using HttpResponseMessage response = await HttpRetryExecutor.SendAsync(
            token =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.notion.com/v1/pages")
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                request.Headers.Add("Notion-Version", ApiVersion);
                return _http.SendAsync(request, token);
            },
            MaxAttempts,
            delayAsync: RetryDelay,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException("Notion rejected the integration token.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new KeyNotFoundException($"Notion database '{databaseId}' was not found or is not shared with this integration.");
        }

        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("id", out JsonElement id) ? id.GetString() ?? string.Empty : string.Empty;
    }

    /// <summary>Builds the Notion page payload without contacting the API.</summary>
    public string BuildPagePayload(string databaseId, string title, List<TelemetryPacket>? packets) =>
        NotionPagePayloadBuilder.Build(databaseId, title, packets);

    /// <summary>Persists the payload locally so a report survives an outage.</summary>
    public string SaveLocalBackupPayload(string targetPath, string title, List<TelemetryPacket>? packets)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("Backup path must be provided.", nameof(targetPath));
        }

        string payload = BuildPagePayload("offline-backup", title, packets);
        File.WriteAllText(targetPath, payload, TelemetryDashboard.Core.Services.Utf8Files.WithoutBom);
        return targetPath;
    }
}
