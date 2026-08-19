using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TelemetryDashboard.UI.Services;

namespace TelemetryDashboard.UI.Controls;

public partial class PasswordLockOverlay : UserControl
{
    private PasswordLockService? _lockService;

    public event Action? OnUnlocked;

    public PasswordLockOverlay()
    {
        InitializeComponent();
    }

    public void AttachService(PasswordLockService lockService)
    {
        _lockService = lockService;
    }

    private void BtnUnlock_Click(object sender, RoutedEventArgs e)
    {
        TryUnlock();
    }

    private void TxtPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryUnlock();
        }
    }

    private void TryUnlock()
    {
        string pwd = TxtPassword.Password;
        if (_lockService != null)
        {
            if (_lockService.Authenticate(pwd))
            {
                TxtError.Visibility = Visibility.Collapsed;
                TxtPassword.Clear();
                Visibility = Visibility.Collapsed;
                OnUnlocked?.Invoke();
            }
            else
            {
                TxtError.Visibility = Visibility.Visible;
            }
        }
        else
        {
            if (pwd == "admin123")
            {
                TxtError.Visibility = Visibility.Collapsed;
                TxtPassword.Clear();
                Visibility = Visibility.Collapsed;
                OnUnlocked?.Invoke();
            }
            else
            {
                TxtError.Visibility = Visibility.Visible;
            }
        }
    }
}
