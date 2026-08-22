using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace TelemetryDashboard.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ReportCrash(args.ExceptionObject as Exception);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Before base.OnStartup, which is what creates the StartupUri window. StaticResource keeps
        // whatever object it resolved, so the palette has to be in place before anything resolves.
        Services.ThemeService.InstallStoredPaletteAtStartup(Resources, Services.UiSettings.Load());

        base.OnStartup(e);
        ApplyDarkThemeIfAvailable();
    }

    /// <summary>
    /// Reports a crash, and decides whether the process can usefully carry on.
    /// </summary>
    /// <remarks>
    /// Marking every dispatcher exception handled was wrong in one specific case, and it is the
    /// case a user actually met: when the failure happens while the first window is being built,
    /// there is no window to return to. The dialog appeared, the user dismissed it, and the process
    /// stayed alive with no visible window and no way to reach it — three of them were found
    /// running at once. A startup failure has to end the process.
    ///
    /// Once a window is up the opposite is true. A binding that throws while rendering one panel
    /// must not take down a session an operator has had open for hours, so those stay handled.
    /// </remarks>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        ReportCrash(args.Exception);
        args.Handled = true;

        if (!HasVisibleWindow())
        {
            Shutdown(ExitStartupFailed);
        }
    }

    /// <summary>Exit code for a failure that stopped the application before it had a window.</summary>
    public const int ExitStartupFailed = 3;

    private bool HasVisibleWindow()
    {
        foreach (Window window in Windows)
        {
            if (window.IsVisible) return true;
        }

        return false;
    }

    private static void ReportCrash(Exception? exception)
    {
        if (exception is null) return;

        string path = CrashLogPath();
        string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UNHANDLED CRASH:{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";

        // Writing the log must not itself throw: a read-only install directory would otherwise turn
        // a reportable crash into a silent one.
        try
        {
            File.AppendAllText(path, entry);
        }
        catch (Exception writeFailure) when (writeFailure is IOException or UnauthorizedAccessException)
        {
            path = "(the log could not be written to disk)";
        }

        MessageBox.Show(
            $"오류가 발생했습니다.{Environment.NewLine}{Environment.NewLine}"
            + $"{exception.GetType().Name}: {exception.Message}{Environment.NewLine}{Environment.NewLine}"
            + $"자세한 내용: {path}",
            "Telemetry Dashboard",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    /// <summary>
    /// Where the crash log goes.
    /// </summary>
    /// <remarks>
    /// Beside the executable when that is writable, and in the user's application data otherwise.
    /// A single-file publish can be run from a read-only location such as Program Files, and a
    /// crash report nobody can write is a crash report nobody can send.
    /// </remarks>
    private static string CrashLogPath()
    {
        string beside = Path.Combine(AppContext.BaseDirectory, "crash_log.txt");

        try
        {
            using (File.Open(beside, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { }
            return beside;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TelemetryDashboard");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "crash_log.txt");
        }
    }

    /// <summary>Applies the WPF-UI dark theme when that package is present.</summary>
    /// <remarks>Reflection, so the application still starts if the optional package is absent.</remarks>
    private static void ApplyDarkThemeIfAvailable()
    {
        try
        {
            Type? manager = Type.GetType("Wpf.Ui.Appearance.ApplicationThemeManager, Wpf.Ui");
            if (manager is null) return;

            Type? themeEnum = Type.GetType("Wpf.Ui.Appearance.ApplicationTheme, Wpf.Ui");
            object darkTheme = themeEnum is not null ? Enum.Parse(themeEnum, "Dark") : 1;

            (manager.GetMethod("Apply", new[] { themeEnum ?? typeof(object) }) ?? manager.GetMethod("Apply"))
                ?.Invoke(null, new[] { darkTheme });
        }
        catch (Exception)
        {
            // Cosmetic only. The application's own dictionaries already define the dark palette.
        }
    }
}
