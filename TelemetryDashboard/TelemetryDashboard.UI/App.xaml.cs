using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace TelemetryDashboard.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            LogCrash(args.ExceptionObject as Exception);
        };

        this.DispatcherUnhandledException += (s, args) =>
        {
            LogCrash(args.Exception);
            args.Handled = true;
        };

        base.OnStartup(e);

        try
        {
            var managerType = Type.GetType("Wpf.Ui.Appearance.ApplicationThemeManager, Wpf.Ui");
            if (managerType != null)
            {
                var themeEnumType = Type.GetType("Wpf.Ui.Appearance.ApplicationTheme, Wpf.Ui");
                var darkValue = themeEnumType != null ? Enum.Parse(themeEnumType, "Dark") : 1;
                var applyMethod = managerType.GetMethod("Apply", new[] { themeEnumType ?? typeof(object) })
                               ?? managerType.GetMethod("Apply");
                applyMethod?.Invoke(null, new[] { darkValue });
            }
        }
        catch
        {
            // Safe fallback
        }
    }

    private static void LogCrash(Exception? ex)
    {
        if (ex == null) return;
        string crashPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
        string content = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UNHANDLED CRASH:\n{ex}\n\n";
        File.AppendAllText(crashPath, content);
        MessageBox.Show($"TelemetryDashboard 실행 중 예외가 발생했습니다.\n\n오류 내용: {ex.Message}\n\n자세한 로그: {crashPath}", "TelemetryDashboard Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
