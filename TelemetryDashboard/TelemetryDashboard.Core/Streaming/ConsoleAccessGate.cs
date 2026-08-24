using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using TelemetryDashboard.Core.Security;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// The one credential this console will accept, checked on every request that reaches it.
/// </summary>
/// <remarks>
/// Built before the door it locks. The console binds loopback only, and the argument for opening it
/// to a network has always stalled on the same sentence: the endpoint has no authentication. Adding
/// the binding first and the check afterwards would have shipped an operator a flag that reads like
/// a lock and is not one, which is worse than no flag at all.
/// <para>
/// One credential, not accounts. This is a bench instrument being reached by the person standing at
/// it or by a proxy in front of it, and a user directory would be a second system to get wrong. The
/// username in a Basic header is therefore read and ignored, deliberately, rather than silently
/// treated as meaningful.
/// </para>
/// <para>
/// HTTP Basic because it is what <c>curl -u</c>, a browser and a reverse proxy all already speak.
/// It is base64, not encryption: on a cleartext link the password is readable by anything on the
/// path, which is exactly why the binding stays loopback until an operator puts TLS in front. The
/// gate does not pretend otherwise and the start-up banner says it out loud.
/// </para>
/// </remarks>
public sealed class ConsoleAccessGate
{
    private readonly PasswordCredential _credential;

    /// <summary>Digests of secrets already proven against the credential this run.</summary>
    /// <remarks>
    /// PBKDF2 at 210,000 iterations is about a tenth of a second, and a console polls several
    /// endpoints a second: paying the derivation on every request would make the product unusable
    /// and would be a denial-of-service anybody could trigger from outside. So a secret is derived
    /// once and remembered as a digest for the life of the process.
    /// <para>
    /// Only successes are cached. A wrong password pays the full derivation every time, which is
    /// the rate limit an endpoint with one credential needs and gets for free.
    /// </para>
    /// </remarks>
    private readonly HashSet<string> _proven = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public ConsoleAccessGate(PasswordCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        _credential = credential;
    }

    /// <summary>The realm offered in a challenge, and what an operator types into the prompt.</summary>
    public const string Realm = "TelemetryDashboard console";

    /// <summary>Whether an <c>Authorization</c> header carries the credential this console accepts.</summary>
    public bool Allows(string? authorization)
    {
        string? secret = SecretIn(authorization);
        if (secret is null) return false;

        string digest = Digest(secret);

        lock (_gate)
        {
            if (_proven.Contains(digest)) return true;
        }

        if (!_credential.Verify(secret)) return false;

        lock (_gate)
        {
            _proven.Add(digest);
        }

        return true;
    }

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
