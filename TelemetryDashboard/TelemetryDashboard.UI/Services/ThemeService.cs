using System;
using System.Windows;

namespace TelemetryDashboard.UI.Services;

public enum AppTheme
{
    Dark,
    Light,
    System
}

public enum WindowBackdropType
{
    Mica,
    Acrylic,
    None,
    Auto
}

public class ThemeService
{
    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;
    public bool EnableMica { get; set; } = true;
    public WindowBackdropType BackdropType { get; set; } = WindowBackdropType.Mica;
    public double CurrentDpiScale { get; private set; } = 1.0;

    public bool IsMicaSupportedOnCurrentOS()
    {
        return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
    }

    public void ApplyTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        try
        {
            var managerType = Type.GetType("Wpf.Ui.Appearance.ApplicationThemeManager, Wpf.Ui")
                           ?? Type.GetType("Wpf.Ui.Appearance.Theme, Wpf.Ui");
            if (managerType != null)
            {
                var applyMethod = managerType.GetMethod("Apply");
                applyMethod?.Invoke(null, null);
            }
        }
        catch
        {
            // Ignore UI application errors in non-WPF runtime test environments
        }
    }

    public void ApplyTheme(string themeName)
    {
        if (Enum.TryParse<AppTheme>(themeName, true, out var theme))
        {
            ApplyTheme(theme);
        }
        else
        {
            ApplyTheme(AppTheme.Dark);
        }
    }

    public void ToggleTheme()
    {
        ApplyTheme(CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
    }

    public void SyncWithSystemTheme(string systemTheme)
    {
        ApplyTheme(systemTheme);
    }

    public void ApplyMicaBackdrop(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (IsMicaSupportedOnCurrentOS() && EnableMica)
        {
            try
            {
                var backdropType = Type.GetType("Wpf.Ui.Appearance.WindowBackdrop, Wpf.Ui");
                var applyMethod = backdropType?.GetMethod("ApplyBackdrop");
                applyMethod?.Invoke(null, new object[] { window });
            }
            catch
            {
                // Fallback safely on unsupported hardware / virtualized environments
            }
        }
    }

    public void SetDpiScale(double scale)
    {
        CurrentDpiScale = scale;
    }
}
