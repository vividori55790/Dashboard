using System;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;

namespace TelemetryDashboard.UI.Services;

public class PasswordLockService : INotifyPropertyChanged
{
    private readonly int _maxAttempts;
    private string? _hashedPasswordHex;

    public bool IsEngineerModeUnlocked { get; private set; } = false;
    public bool IsEngineerMode => IsEngineerModeUnlocked;
    public string CurrentMode => IsEngineerModeUnlocked ? "EngineerMode" : "OperatorView";
    public bool IsLockedOut { get; private set; } = false;
    public int FailedAttempts { get; private set; } = 0;
    public bool IsPasswordConfigured => !string.IsNullOrEmpty(_hashedPasswordHex);

    public event PropertyChangedEventHandler? PropertyChanged;

    public PasswordLockService(int maxAttempts = 3)
    {
        _maxAttempts = maxAttempts;
        _hashedPasswordHex = null;
    }

    public void SetPassword(string password)
    {
        if (string.IsNullOrEmpty(password)) return;
        _hashedPasswordHex = HashPassword(password);
        NotifyStateChanged();
    }

    public bool Authenticate(string password)
    {
        if (IsLockedOut) return false;

        if (string.IsNullOrEmpty(password))
        {
            RegisterFailedAttempt();
            return false;
        }

        string inputHash = HashPassword(password);

        if (_hashedPasswordHex == null)
        {
            if (password == "admin123")
            {
                _hashedPasswordHex = inputHash;
                IsEngineerModeUnlocked = true;
                FailedAttempts = 0;
                NotifyStateChanged();
                return true;
            }
            else
            {
                RegisterFailedAttempt();
                return false;
            }
        }

        if (inputHash == _hashedPasswordHex)
        {
            IsEngineerModeUnlocked = true;
            FailedAttempts = 0;
            NotifyStateChanged();
            return true;
        }

        RegisterFailedAttempt();
        return false;
    }

    public bool TryUnlock(string password) => Authenticate(password);

    public void LockEngineerMode()
    {
        IsEngineerModeUnlocked = false;
        NotifyStateChanged();
    }

    public void Lock() => LockEngineerMode();

    public bool CanAccessEngineerView()
    {
        return IsEngineerModeUnlocked && !IsLockedOut;
    }

    private void RegisterFailedAttempt()
    {
        FailedAttempts++;
        if (FailedAttempts >= _maxAttempts)
        {
            IsLockedOut = true;
        }
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private static string HashPassword(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
