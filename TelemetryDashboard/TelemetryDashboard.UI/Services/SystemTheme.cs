using System;
using Microsoft.Win32;

namespace TelemetryDashboard.UI.Services;

/// <summary>
/// What Windows itself is set to, and being told when that changes.
/// </summary>
/// <remarks>
/// <c>AppTheme.System</c> was a value in the enum that nothing implemented. It parsed, it stored,
/// and then <c>ThemePalette.For(theme == Light)</c> quietly resolved it to Dark — so an operator
/// who asked the application to follow Windows got the dark palette whatever Windows was set to,
/// and nothing anywhere said otherwise. This is the missing half.
/// <para>
/// Windows records the choice for applications separately from the one for its own chrome:
/// <c>AppsUseLightTheme</c> is the app setting and <c>SystemUsesLightTheme</c> is the taskbar and
/// Start menu. Following the second would put this window in a palette the operator chose for
/// something else, so only the first is read.
/// </para>
/// </remarks>
public static class SystemTheme
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Reads whether Windows currently asks applications to be light.</summary>
    /// <remarks>
    /// False when the value is missing rather than a guess in either direction. A machine that has
    /// never been told keeps the palette this application ships in, which is what the operator has
    /// been looking at until the moment they chose to follow the system.
    /// </remarks>
    public static bool TryReadIsLight(out bool light)
    {
        light = false;

        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            if (key?.GetValue("AppsUseLightTheme") is not int value) return false;

            light = value != 0;
            return true;
        }
        catch (Exception failure) when (failure is System.Security.SecurityException
                                        or UnauthorizedAccessException
                                        or System.IO.IOException)
        {
            // A locked-down machine can refuse the read. Following the system is then simply
            // unavailable, which the caller reports; it is not a reason to fail to start.
            return false;
        }
    }

    /// <summary>Whether Windows is set to light, treating an unreadable setting as dark.</summary>
    public static bool IsLight() => TryReadIsLight(out bool light) && light;

    /// <summary>
    /// Calls back whenever Windows changes its application theme, until disposed.
    /// </summary>
    /// <remarks>
    /// <c>SystemEvents</c> holds the handler in a static list and raises it on a thread of its own,
    /// so two things have to be right or this becomes a bug rather than a feature: the subscription
    /// has to end when the operator stops following the system — otherwise a later Windows change
    /// would overrule a palette they picked by hand — and the callback has to reach the UI thread
    /// before it touches a brush. The returned handle does the first; the caller does the second.
    /// </remarks>
    public static IDisposable Watch(Action<bool> onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);
        return new Subscription(onChanged);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action<bool> _onChanged;
        private bool _disposed;

        internal Subscription(Action<bool> onChanged)
        {
            _onChanged = onChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // General is the category Windows raises for a light/dark switch. Reading the registry
            // again rather than trusting the category: other things share it.
            if (e.Category != UserPreferenceCategory.General) return;

            _onChanged(IsLight());
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
    }
}
