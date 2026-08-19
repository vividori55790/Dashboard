using System;
using System.Collections.Generic;
using System.Linq;

namespace TelemetryDashboard.Infrastructure.WebServer;

/// <summary>Shape checks for the credentials and identifiers <see cref="NotionClient"/> is given.</summary>
/// <remarks>
/// Split out of the client so validation stays reachable without an <c>HttpClient</c> and cannot
/// drift into the request path. The checks are deliberately about <em>shape</em> only: whether a
/// token or database id could possibly be genuine. Whether it is genuine is Notion's answer to
/// give, and these throw the same exception types the API's rejections map to, so a caller
/// handles a local typo and a remote rejection identically.
/// </remarks>
internal static class NotionCredentialValidator
{
    private const int MinimumLiveTokenLength = 40;

    private static readonly string[] LiveTokenPrefixes = { "secret_", "ntn_" };

    /// <summary>True when the token claims to be a real Notion credential.</summary>
    internal static bool IsLiveToken(string token) =>
        LiveTokenPrefixes.Any(p => token.StartsWith(p, StringComparison.Ordinal));

    /// <summary>Rejects a token that claims to be live but is too short to be one.</summary>
    internal static void ValidateToken(string token)
    {
        if (IsLiveToken(token) && token.Length < MinimumLiveTokenLength)
        {
            throw new UnauthorizedAccessException(
                "Notion token is malformed: a live integration secret is longer than this.");
        }
    }

    /// <summary>Notion identifiers are 32 hex digits, optionally dash-separated.</summary>
    internal static void ValidateDatabaseId(string databaseId)
    {
        string compact = (databaseId ?? string.Empty).Replace("-", string.Empty);

        bool wellFormed = compact.Length == 32 && compact.All(Uri.IsHexDigit);
        if (!wellFormed)
        {
            throw new KeyNotFoundException(
                $"'{databaseId}' is not a Notion database id (expected 32 hexadecimal characters).");
        }
    }
}
