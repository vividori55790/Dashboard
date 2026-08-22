using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TelemetryDashboard.Core.Security;

/// <summary>
/// One stored password, as a salted PBKDF2 derivation rather than as the password.
/// </summary>
/// <remarks>
/// What this replaces was security theatre, and the shape of it is worth recording because it is a
/// shape that reads as correct. The lock service hashed the input with SHA-256 on the line above
/// the check, and then checked <c>password == "admin123"</c> against a literal — the hash was
/// computed, stored afterwards, and played no part in the decision. Code that hashes is not code
/// that verifies a hash, and the two are hard to tell apart at a glance.
/// <para>
/// Three properties this has and that did not:
/// </para>
/// <list type="bullet">
/// <item>A per-credential random salt, so two installations with the same password do not produce
/// the same stored value, and a precomputed table is useless.</item>
/// <item>A work factor. Bare SHA-256 is designed to be fast, which is the opposite of what a
/// password needs; PBKDF2 at 210,000 iterations makes each guess cost about as much as a hundred
/// thousand of them did.</item>
/// <item>A fixed-time comparison. String <c>==</c> returns as soon as two bytes differ, and the
/// time it took is a measurement of how much of the hash was right.</item>
/// </list>
/// <para>
/// This is a local screen lock, not an authentication server: it keeps someone who walks up to an
/// unattended machine from reading the plant's telemetry. It is not a defence against an attacker
/// who has the file and unlimited time, and nothing here should be read as claiming otherwise.
/// </para>
/// </remarks>
public sealed class PasswordCredential
{
    /// <summary>OWASP's 2023 floor for PBKDF2-HMAC-SHA256, and cheap enough to be unnoticed once.</summary>
    public const int DefaultIterations = 210_000;

    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const string Version = "v1";

    private readonly byte[] _salt;
    private readonly byte[] _hash;

    private PasswordCredential(byte[] salt, byte[] hash, int iterations)
    {
        _salt = salt;
        _hash = hash;
        Iterations = iterations;
    }

    public int Iterations { get; }

    /// <summary>Shortest password this will store.</summary>
    /// <remarks>
    /// Stated as a constant so the enrollment screen and the store cannot disagree about it. A
    /// rule the UI enforces and the store does not is a rule that any other caller skips.
    /// </remarks>
    public const int MinimumLength = 8;

    /// <summary>Derives a credential from a password, with a fresh random salt.</summary>
    /// <exception cref="ArgumentException">The password is shorter than <see cref="MinimumLength"/>.</exception>
    public static PasswordCredential Create(string password, int iterations = DefaultIterations)
    {
        if (password is null || password.Length < MinimumLength)
        {
            throw new ArgumentException(
                $"A password needs at least {MinimumLength} characters.", nameof(password));
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        return new PasswordCredential(salt, Derive(password, salt, iterations), iterations);
    }

    /// <summary>Whether this password produces the stored derivation.</summary>
    public bool Verify(string? password)
    {
        if (string.IsNullOrEmpty(password)) return false;

        byte[] candidate = Derive(password, _salt, Iterations);

        // Fixed time. The obvious `SequenceEqual` returns at the first differing byte, and how long
        // that took says how many leading bytes were right — which is enough to find the rest one
        // byte at a time.
        return CryptographicOperations.FixedTimeEquals(candidate, _hash);
    }

    /// <summary>The credential as one line of text, safe to write to disk.</summary>
    public string ToStorage() => string.Create(CultureInfo.InvariantCulture,
        $"{Version}${Iterations}${Convert.ToBase64String(_salt)}${Convert.ToBase64String(_hash)}");

    /// <summary>Reads back <see cref="ToStorage"/>, refusing anything it does not recognise.</summary>
    /// <remarks>
    /// Refuses rather than repairs. A credential file that has been truncated, half-written or
    /// hand-edited must not silently become a credential that accepts something — the failure mode
    /// of a lock is that it opens.
    /// </remarks>
    public static bool TryParse(string? stored, out PasswordCredential credential)
    {
        credential = null!;
        if (string.IsNullOrWhiteSpace(stored)) return false;

        string[] parts = stored.Trim().Split('$');
        if (parts.Length != 4 || parts[0] != Version) return false;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int iterations)
            || iterations < 1000)
        {
            return false;
        }

        byte[] salt, hash;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            hash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length != SaltBytes || hash.Length != HashBytes) return false;

        credential = new PasswordCredential(salt, hash, iterations);
        return true;
    }

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, HashBytes);
}
