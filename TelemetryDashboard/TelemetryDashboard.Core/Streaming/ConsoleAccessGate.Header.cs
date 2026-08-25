using System;
using System.Security.Cryptography;
using System.Text;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// Getting a password out of an <c>Authorization</c> header, and reducing it to a digest.
/// </summary>
/// <remarks>
/// Split from the decision half because the two have different threat surfaces. Everything
/// here runs on bytes from an unauthenticated client -- it is the first code a stranger
/// reaches -- while the other half only ever sees a string this one has already made sense of.
/// </remarks>
public sealed partial class ConsoleAccessGate
{
    /// <summary>The password inside a Basic header, or null when there is not one to read.</summary>
    /// <remarks>
    /// A malformed header answers null rather than throwing. Anything reachable before
    /// authentication is reachable by anyone, so it has to survive being sent rubbish on purpose.
    /// </remarks>
    private static string? SecretIn(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization)) return null;

        const string scheme = "Basic ";
        if (!authorization.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return null;

        try
        {
            string pair = Encoding.UTF8.GetString(Convert.FromBase64String(authorization[scheme.Length..].Trim()));

            // The username is read and ignored: there is one credential here, and pretending the
            // name selects something would invite somebody to rely on it later.
            int separator = pair.IndexOf(':');
            return separator < 0 ? null : pair[(separator + 1)..];
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException or ArgumentException)
        {
            return null;
        }
    }

    private static string Digest(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
