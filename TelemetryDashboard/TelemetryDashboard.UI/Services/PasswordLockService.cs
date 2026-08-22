using System;
using System.ComponentModel;
using TelemetryDashboard.Core.Security;

namespace TelemetryDashboard.UI.Services;

/// <summary>
/// The screen lock's state: whether a password exists, and whether this session is unlocked.
/// </summary>
/// <remarks>
/// This used to accept the literal <c>admin123</c>. Not as a seed, not as a first-run default that
/// prompted for a replacement — as the credential, on every launch, because the field holding the
/// hash was per-process and started null, so the branch comparing against the literal was the one
/// that ran every real time. A second copy of the same literal sat in the overlay's code-behind and
/// would accept it with no service attached at all.
/// <para>
/// There is now no default password. An installation either has one, or it does not and the lock
/// screen asks the operator to choose one. That is the honest answer to "what is the initial
/// password": there isn't one, and there never should have been, because a documented default on a
/// plant machine is the same as no lock.
/// </para>
/// <para>
/// The parts worth testing — deriving, verifying, counting failures, cooling down — live in
/// <see cref="ScreenLockPolicy"/> and <see cref="PasswordCredential"/> in the portable half, so
/// they are covered by the suite that runs everywhere rather than by the desktop-only one.
/// </para>
/// </remarks>
public class PasswordLockService : INotifyPropertyChanged
{
    private readonly string _credentialPath;
    private readonly ScreenLockPolicy _policy;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PasswordLockService(string? credentialPath = null, int maxAttempts = 3)
    {
        _credentialPath = credentialPath ?? CredentialFile.DefaultPath;
        _policy = new ScreenLockPolicy(CredentialFile.Load(_credentialPath), maxAttempts);
    }

    /// <summary>Whether this session has been unlocked.</summary>
    public bool IsEngineerModeUnlocked { get; private set; }

    public bool IsEngineerMode => IsEngineerModeUnlocked;

    public string CurrentMode => IsEngineerModeUnlocked ? "EngineerMode" : "OperatorView";

    /// <summary>Whether a password has been set on this installation.</summary>
    public bool IsPasswordConfigured => _policy.IsConfigured;

    /// <summary>Wrong answers left before the next wait.</summary>
    public int RemainingAttempts => _policy.RemainingAttempts;

    /// <summary>How long the operator has to wait, or zero.</summary>
    public TimeSpan CooldownRemaining => _policy.CooldownRemaining(DateTime.UtcNow);

    /// <summary>Whether a wait is in progress.</summary>
    public bool IsCoolingDown => CooldownRemaining > TimeSpan.Zero;

    /// <summary>Where the credential is kept, so the UI can tell the operator.</summary>
    public string CredentialPath => _credentialPath;

    /// <summary>
    /// Sets the password and writes it where a restart will find it.
    /// </summary>
    /// <returns>Null when it was stored, or why it was not.</returns>
    public string? SetPassword(string password)
    {
        PasswordCredential credential;
        try
        {
            credential = PasswordCredential.Create(password);
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }

        // Written first. A credential accepted in memory but not on disk is one that disappears at
        // the next restart, which is the defect this whole change exists to remove.
        string? failure = CredentialFile.Save(_credentialPath, credential);
        if (failure is not null) return $"The password could not be saved: {failure}";

        // Storing a credential is not authenticating with it. Unlocking here as a side effect made
        // "set a password" and "prove you know the password" the same act, and a wrong password
        // afterwards then found the session already open.
        _policy.SetCredential(credential);
        NotifyStateChanged();
        return null;
    }

    /// <summary>Checks a password. See <see cref="UnlockOutcome"/> for what the answer means.</summary>
    public UnlockOutcome TryUnlock(string? password)
    {
        UnlockOutcome outcome = _policy.Authenticate(password, DateTime.UtcNow);
        if (outcome == UnlockOutcome.Unlocked) IsEngineerModeUnlocked = true;
        NotifyStateChanged();
        return outcome;
    }

    /// <summary>Kept for callers that only want to know whether it opened.</summary>
    public bool Authenticate(string password) => TryUnlock(password) == UnlockOutcome.Unlocked;

    public void LockEngineerMode()
    {
        IsEngineerModeUnlocked = false;
        NotifyStateChanged();
    }

    public void Lock() => LockEngineerMode();

    public bool CanAccessEngineerView() => IsEngineerModeUnlocked && !IsCoolingDown;

    private void NotifyStateChanged() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
