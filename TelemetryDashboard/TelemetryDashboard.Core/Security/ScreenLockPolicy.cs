using System;

namespace TelemetryDashboard.Core.Security;

/// <summary>What happened when a password was offered.</summary>
public enum UnlockOutcome
{
    /// <summary>Correct; the screen opens.</summary>
    Unlocked,

    /// <summary>Wrong password. <see cref="ScreenLockPolicy.RemainingAttempts"/> says how many are left.</summary>
    Rejected,

    /// <summary>Too many wrong answers; nothing is checked until the cooldown expires.</summary>
    CoolingDown,

    /// <summary>No password has ever been set on this installation, so there is nothing to check.</summary>
    NotConfigured
}

/// <summary>
/// How many wrong answers are allowed, and what happens after them.
/// </summary>
/// <remarks>
/// The previous rule was a one-way latch: three wrong answers set <c>IsLockedOut = true</c>, and
/// nothing anywhere ever set it back. Every later call returned false regardless of the password,
/// with no message explaining why, so an operator who mistyped three times had no way back into
/// their own dashboard except killing the process — a denial of service written into the lock, and
/// one that no test noticed because no test tried a fourth time.
/// <para>
/// A cooldown instead, doubling each round to a ceiling. Guessing gets slow quickly, and someone
/// who simply mistyped waits rather than restarting. Time is passed in rather than read, so the
/// escalation can be tested without a test that sleeps.
/// </para>
/// </remarks>
public sealed class ScreenLockPolicy
{
    private readonly int _attemptsPerRound;
    private readonly TimeSpan _firstCooldown;
    private readonly TimeSpan _maximumCooldown;

    private PasswordCredential? _credential;
    private int _failures;
    private int _rounds;
    private DateTime? _coolingUntilUtc;

    public ScreenLockPolicy(
        PasswordCredential? credential = null,
        int attemptsPerRound = 3,
        TimeSpan? firstCooldown = null,
        TimeSpan? maximumCooldown = null)
    {
        _credential = credential;
        _attemptsPerRound = Math.Max(1, attemptsPerRound);
        _firstCooldown = firstCooldown ?? TimeSpan.FromSeconds(15);
        _maximumCooldown = maximumCooldown ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>Whether this installation has a password at all.</summary>
    public bool IsConfigured => _credential is not null;

    /// <summary>Wrong answers left before the next cooldown.</summary>
    public int RemainingAttempts => Math.Max(0, _attemptsPerRound - _failures);

    /// <summary>When the current cooldown ends, or null when there is not one.</summary>
    public DateTime? CoolingUntilUtc => _coolingUntilUtc;

    /// <summary>Sets or replaces the password, and clears any cooldown.</summary>
    /// <remarks>
    /// Clearing the cooldown is deliberate: setting a password is something only an already
    /// unlocked session can do, so it is proof of authority rather than a way around the wait.
    /// </remarks>
    public void SetCredential(PasswordCredential credential)
    {
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
        _failures = 0;
        _rounds = 0;
        _coolingUntilUtc = null;
    }

    /// <summary>How long is left to wait, or <see cref="TimeSpan.Zero"/> when nothing is.</summary>
    public TimeSpan CooldownRemaining(DateTime nowUtc) =>
        _coolingUntilUtc is { } until && until > nowUtc ? until - nowUtc : TimeSpan.Zero;

    /// <summary>Checks a password against the stored credential.</summary>
    public UnlockOutcome Authenticate(string? password, DateTime nowUtc)
    {
        if (_credential is null) return UnlockOutcome.NotConfigured;

        if (CooldownRemaining(nowUtc) > TimeSpan.Zero) return UnlockOutcome.CoolingDown;

        // The wait is over, so the count starts again. Left in place it would mean one wrong answer
        // after a cooldown triggered the next one immediately.
        if (_coolingUntilUtc is not null)
        {
            _coolingUntilUtc = null;
            _failures = 0;
        }

        if (_credential.Verify(password))
        {
            _failures = 0;
            _rounds = 0;
            return UnlockOutcome.Unlocked;
        }

        _failures++;
        if (_failures < _attemptsPerRound) return UnlockOutcome.Rejected;

        // Doubling, capped. Ticks rather than seconds so a sub-second cooldown in a test does not
        // round away to nothing.
        long ticks = _firstCooldown.Ticks * (long)Math.Pow(2, Math.Min(_rounds, 20));
        _coolingUntilUtc = nowUtc + TimeSpan.FromTicks(Math.Min(ticks, _maximumCooldown.Ticks));
        _rounds++;
        return UnlockOutcome.CoolingDown;
    }
}
