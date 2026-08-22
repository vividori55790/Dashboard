using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TelemetryDashboard.Core.Security;
using TelemetryDashboard.UI.Services;

namespace TelemetryDashboard.UI.Controls;

/// <summary>
/// Asks for the screen-lock password, or — on an installation that has none — for a new one.
/// </summary>
/// <remarks>
/// This file used to contain its own copy of the credential: if no service had been attached it
/// compared the input against the literal <c>admin123</c> and opened. A second gate, with no
/// attempt counting and no cooldown, that any refactor moving the attach call would silently
/// re-arm. It now fails closed — with no service there is nothing to check against, and the
/// correct answer to a password is "no".
/// </remarks>
public partial class PasswordLockOverlay : UserControl
{
    private PasswordLockService? _lockService;
    private DispatcherTimer? _cooldownTicker;

    public event Action? OnUnlocked;

    /// <summary>True while this panel is asking the operator to choose a password.</summary>
    public bool IsEnrolling { get; private set; }

    public PasswordLockOverlay()
    {
        InitializeComponent();
    }

    public void AttachService(PasswordLockService lockService)
    {
        _lockService = lockService;
    }

    /// <summary>
    /// Puts the panel into the right mode and takes the keyboard.
    /// </summary>
    /// <remarks>
    /// Focus is the part that was missing everywhere in this application's overlays: a panel that
    /// appears without it looks ready and swallows nothing, so the operator types their password
    /// into whatever had focus behind it. Called from IsVisibleChanged rather than from the caller,
    /// so every way of showing this panel gets it.
    /// </remarks>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible)
        {
            StopTicker();
            return;
        }

        IsEnrolling = _lockService is { IsPasswordConfigured: false };

        TxtPassword.Clear();
        TxtConfirm.Clear();
        TxtError.Visibility = Visibility.Collapsed;
        ConfirmPanel.Visibility = IsEnrolling ? Visibility.Visible : Visibility.Collapsed;

        if (IsEnrolling)
        {
            TxtTitle.Text = "Set a screen-lock password";
            TxtCaption.Text = "This installation has no password yet. There is no default one — "
                            + "choose the password this machine will use.";
            LblPassword.Text = $"New password (at least {PasswordCredential.MinimumLength} characters)";
            BtnAction.Content = "Set password";
            ShowHint(_lockService is null
                ? null
                : "Stored on this machine only, at " + _lockService.CredentialPath);
        }
        else
        {
            TxtTitle.Text = "Screen locked";
            TxtCaption.Text = "The dashboard keeps running. Enter the password to get back to it.";
            LblPassword.Text = "Password";
            BtnAction.Content = "Unlock";
            ShowHint(null);
        }

        // Queued at input priority: the panel is not yet arranged at the moment visibility flips,
        // and Focus() on an unrealised element quietly does nothing.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => TxtPassword.Focus()));
        RefreshCooldown();
    }

    private void BtnUnlock_Click(object sender, RoutedEventArgs e) => Submit();

    private void TxtPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        // Enter in the first box during enrollment moves to the second rather than submitting a
        // half-typed pair.
        if (IsEnrolling && ReferenceEquals(sender, TxtPassword))
        {
            TxtConfirm.Focus();
            e.Handled = true;
            return;
        }

        Submit();
        e.Handled = true;
    }

    private void Submit()
    {
        if (_lockService is null)
        {
            // Fail closed. The alternative this replaces was a hard-coded password.
            Fail("This lock has no credential store attached, so nothing can be checked. "
               + "Restart the application.");
            return;
        }

        if (IsEnrolling) SubmitNewPassword(_lockService);
        else SubmitUnlock(_lockService);
    }

    private void SubmitNewPassword(PasswordLockService service)
    {
        string password = TxtPassword.Password;

        if (!string.Equals(password, TxtConfirm.Password, StringComparison.Ordinal))
        {
            Fail("The two passwords are not the same.");
            TxtConfirm.Clear();
            TxtConfirm.Focus();
            return;
        }

        if (service.SetPassword(password) is { } problem)
        {
            Fail(problem);
            return;
        }

        // Through the same door as everyone else: the password just chosen is checked against what
        // was just stored. If that ever fails, the store and the check disagree, and finding that
        // out here is far better than at the next restart with nobody able to get in.
        if (service.TryUnlock(password) != UnlockOutcome.Unlocked)
        {
            Fail("The password was saved but did not unlock. Do not restart; report this.");
            return;
        }

        Dismiss();
    }

    private void SubmitUnlock(PasswordLockService service)
    {
        switch (service.TryUnlock(TxtPassword.Password))
        {
            case UnlockOutcome.Unlocked:
                Dismiss();
                return;

            case UnlockOutcome.NotConfigured:
                // The credential file went away between locking and unlocking. Turn into the
                // enrollment panel rather than refusing every password with no way forward.
                Fail("There is no password on this installation any more. Set one to continue.");
                OnIsVisibleChanged(this, default);
                return;

            case UnlockOutcome.CoolingDown:
                RefreshCooldown();
                return;

            default:
                Fail(service.RemainingAttempts == 1
                    ? "That password was not accepted. One more attempt before a pause."
                    : $"That password was not accepted. {service.RemainingAttempts} attempts left.");
                TxtPassword.SelectAll();
                TxtPassword.Focus();
                return;
        }
    }

    private void Dismiss()
    {
        TxtError.Visibility = Visibility.Collapsed;
        TxtPassword.Clear();
        TxtConfirm.Clear();
        Visibility = Visibility.Collapsed;
        OnUnlocked?.Invoke();
    }

    private void Fail(string message)
    {
        TxtError.Text = message;
        TxtError.Visibility = Visibility.Visible;
    }

    private void ShowHint(string? message)
    {
        TxtHint.Text = message ?? string.Empty;
        TxtHint.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Counts the wait down on screen instead of refusing silently.
    /// </summary>
    /// <remarks>
    /// The old lock had no way out at all after three wrong answers, and said nothing about it. A
    /// wait an operator can see is a wait they will sit through; a button that has stopped working
    /// for no stated reason is a support call.
    /// </remarks>
    private void RefreshCooldown()
    {
        if (_lockService is null) return;

        TimeSpan left = _lockService.CooldownRemaining;
        if (left <= TimeSpan.Zero)
        {
            bool wasWaiting = _cooldownTicker is not null;
            StopTicker();
            BtnAction.IsEnabled = true;
            TxtPassword.IsEnabled = true;

            if (wasWaiting)
            {
                // Both of these were found by sitting through an actual cooldown rather than by
                // reading the code. The countdown stopped at its last drawn value -- "Try again in
                // 1 s" -- and stayed there, because ending the wait re-enabled the controls and
                // never cleared the message that explained why they had been disabled. And
                // disabling a focused control moves focus off it, so when the wait ended the
                // operator was typing their password into nothing, which is the very defect this
                // panel was rewritten to remove.
                TxtError.Visibility = Visibility.Collapsed;
                TxtPassword.Focus();
            }

            return;
        }

        BtnAction.IsEnabled = false;
        TxtPassword.IsEnabled = false;
        Fail(string.Create(CultureInfo.InvariantCulture,
            $"Too many attempts. Try again in {Math.Ceiling(left.TotalSeconds):F0} s."));

        if (_cooldownTicker is not null) return;
        _cooldownTicker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _cooldownTicker.Tick += (_, _) => RefreshCooldown();
        _cooldownTicker.Start();
    }

    private void StopTicker()
    {
        _cooldownTicker?.Stop();
        _cooldownTicker = null;
    }
}
