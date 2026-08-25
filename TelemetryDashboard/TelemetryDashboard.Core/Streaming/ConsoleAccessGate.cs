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
/// Built before the door it locks. The argument for opening this console to a network had always
/// stalled on the same sentence -- the endpoint has no authentication -- and adding the binding
/// first would have shipped an operator a flag that reads like a lock and is not one, which is
/// worse than no flag at all. <c>--listen network</c> came afterwards and cannot be asked for
/// without this.
/// <para>
/// One credential, not accounts. This is a bench instrument being reached by the person standing at
/// it or by a proxy in front of it, and a user directory would be a second system to get wrong. The
/// username in a Basic header is therefore read and ignored, deliberately, rather than silently
/// treated as meaningful.
/// </para>
/// <para>
/// HTTP Basic because it is what <c>curl -u</c>, a browser and a reverse proxy all already speak.
/// It is base64, not encryption: on a cleartext link the password is readable by anything on the
/// path. That is the whole of what this gives an operator, and it is why <c>--listen network</c>
/// requires this gate and still carries a warning at every launch -- the credential decides who
/// may connect, and nothing here decides who may read what crosses the wire. The gate does not
/// pretend otherwise, the banner says it out loud, and <c>/api/status</c> reports
/// <c>encrypted: false</c> so a consumer is not left to infer it.
/// </para>
/// </remarks>
public sealed partial class ConsoleAccessGate
{
    private readonly PasswordCredential _credential;

    /// <summary>Digests of secrets already proven against the credential this run.</summary>
    /// <remarks>
    /// PBKDF2 at 210,000 iterations is about a tenth of a second, and a console polls several
    /// endpoints a second: paying the derivation on every request would make the product unusable
    /// and would be a denial-of-service anybody could trigger from outside. So a secret is derived
    /// once and remembered as a digest for the life of the process.
    /// <para>
    /// Successes are cached without a ceiling because there is one secret that can land here.
    /// Failures are cached too, and differently -- see <see cref="_refused"/>, which is bounded
    /// precisely because anyone may put anything in that set.
    /// </para>
    /// </remarks>
    private readonly HashSet<string> _proven = new(StringComparer.Ordinal);

    /// <summary>Digests of secrets already refused, so a client retrying one does not re-derive.</summary>
    /// <remarks>
    /// The original design deliberately made every failure pay the full derivation, on the
    /// reasoning that it is the rate limit an endpoint with one credential gets for free. That
    /// held while this console was reachable only from its own machine. <c>--listen network</c>
    /// changed who can send a header, and the reasoning only survives half the change: it is right
    /// for a <em>distinct</em> guess and wrong for a repeated one. A client looping on a stale
    /// password — a script holding the old value, a browser re-sending what was typed once — turns
    /// one wrong string into a tenth of a second of CPU per attempt, charged to the process that
    /// is reading the plant. Nobody has to be hostile for that to happen.
    /// <para>
    /// So a repeated wrong secret is refused from memory and a new one still pays in full. Brute
    /// force is exactly the case where the cost must be charged, and it is the case that varies
    /// the guess. Remembering that a particular string is wrong tells an attacker nothing they did
    /// not send.
    /// </para>
    /// <para>
    /// Bounded, and at the cap it stops admitting rather than evicting. Eviction would let anyone
    /// varying their guess push out the entry a legitimate retry loop depends on, which is the one
    /// thing this exists for; refusing to grow just returns the endpoint to how it behaved before.
    /// </para>
    /// </remarks>
    private readonly HashSet<string> _refused = new(StringComparer.Ordinal);
    private readonly int _maxRememberedRefusals;
    private readonly object _gate = new();
    private long _derivations;

    /// <summary>Default ceiling on remembered refusals: about 64 KB of digests.</summary>
    public const int DefaultMaxRememberedRefusals = 1024;

    public ConsoleAccessGate(
        PasswordCredential credential,
        int maxRememberedRefusals = DefaultMaxRememberedRefusals)
    {
        ArgumentNullException.ThrowIfNull(credential);
        _credential = credential;
        _maxRememberedRefusals = maxRememberedRefusals;
    }

    /// <summary>
    /// How many times this gate has run the key derivation since the process started.
    /// </summary>
    /// <remarks>
    /// The expensive thing, counted rather than assumed. It is what the caches above exist to
    /// avoid, so a claim that they work is checkable instead of inferred from a stopwatch — and on
    /// a host that has been reachable from a network for a week, it is the number that says
    /// whether anything has been trying passwords at it.
    /// </remarks>
    public long Derivations
    {
        get { lock (_gate) { return _derivations; } }
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
            if (_refused.Contains(digest)) return false;
        }

        // Outside the lock: the derivation is a tenth of a second, and holding the lock across it
        // would serialise every request behind the first wrong password -- turning the guard
        // against one client's cost into a way for that client to stall all of them.
        bool ok = _credential.Verify(secret);

        lock (_gate)
        {
            _derivations++;
            if (ok)
            {
                _proven.Add(digest);
            }
            else if (_refused.Count < _maxRememberedRefusals)
            {
                _refused.Add(digest);
            }
        }

        return ok;
    }
}
