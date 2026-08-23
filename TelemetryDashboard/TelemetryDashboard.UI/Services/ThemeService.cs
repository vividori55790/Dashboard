using System;
using System.Collections.Generic;
using System.Windows;
using TelemetryDashboard.UI.Themes;

namespace TelemetryDashboard.UI.Services;

/// <summary>Which palette the application paints in.</summary>
/// <remarks>
/// <c>System</c> means what it says: the palette follows the Windows application theme, and keeps
/// following it while Windows is changed. It used to be a value that parsed, stored, and then
/// resolved to Dark.
/// </remarks>
public enum AppTheme
{
    Dark,
    Light,
    System
}

/// <summary>
/// Applies a palette to the running application, and remembers which one.
/// </summary>
/// <remarks>
/// Two earlier versions of this failed in ways worth keeping written down, because both passed
/// their tests.
/// <para>
/// The first reflected for <c>Wpf.Ui.Appearance.ApplicationThemeManager</c>, called
/// <c>GetMethod("Apply")</c> and invoked it with <c>null</c> arguments — the chosen theme was never
/// passed to anything, the ambiguous overload threw into a bare <c>catch</c>, and no XAML in this
/// application references that library, so there was nothing for it to restyle. The log line said
/// "Theme toggled." each time.
/// </para>
/// <para>
/// The second set the <c>Color</c> of each of the 33 brushes in the token dictionary. That is the
/// right idea and it cannot work: brushes in a dictionary loaded from compiled BAML are frozen, so
/// in the running application every one of them had to be replaced instead of repainted — which
/// reaches nothing already on screen, because <c>StaticResource</c> kept the old object. It was at
/// least honest about it, and told the operator to restart.
/// </para>
/// <para>
/// This one changes the <em>colours</em>. Tokens.xaml binds every brush to a colour with
/// <c>DynamicResource</c>, which both keeps the brush unfrozen and makes it follow; a colour is a
/// struct with no identity to lose, so replacing one in the dictionary reaches all 900
/// <c>StaticResource</c> references to the brush that holds it, immediately, with no restart.
/// </para>
/// </remarks>
public sealed partial class ThemeService : IDisposable
{
    private readonly UiSettings _settings;
    private IDisposable? _systemWatch;

    public ThemeService(UiSettings? settings = null)
    {
        _settings = settings ?? UiSettings.Load();
        CurrentTheme = Enum.TryParse(_settings.Theme, ignoreCase: true, out AppTheme stored)
            ? stored
            : AppTheme.Dark;
        EffectiveIsLight = Resolve(CurrentTheme);
    }

    /// <summary>What the operator chose, which may be <see cref="AppTheme.System"/>.</summary>
    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    /// <summary>Which of the two palettes that choice actually paints in right now.</summary>
    public bool EffectiveIsLight { get; private set; }

    /// <summary>True while the palette is tracking the Windows setting.</summary>
    public bool FollowsSystem => CurrentTheme == AppTheme.System;

    /// <summary>Colour keys the palette named that the resource dictionary does not have.</summary>
    /// <remarks>Reported rather than thrown: a mistyped key must not stop the window opening.</remarks>
    public IReadOnlyList<string> UnknownKeys { get; private set; } = Array.Empty<string>();

    /// <summary>How many colours the last apply actually changed.</summary>
    public int ChangedColours { get; private set; }

    /// <summary>Raised after a palette has been installed, on the thread that installed it.</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>Repaints the interface, and stores the choice for the next launch.</summary>
    public void ApplyTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        EffectiveIsLight = Resolve(theme);

        _settings.Theme = theme.ToString();
        _settings.Save();

        // Subscribe only while following, or a later Windows change would overrule a palette the
        // operator picked by hand.
        _systemWatch?.Dispose();
        _systemWatch = theme == AppTheme.System ? SystemTheme.Watch(OnSystemThemeChanged) : null;

        Repaint();
    }

    public void ApplyTheme(string themeName) =>
        ApplyTheme(Enum.TryParse(themeName, ignoreCase: true, out AppTheme theme)
            ? theme
            : AppTheme.Dark);

    /// <summary>Switches between the two explicit palettes, and stops following the system.</summary>
    public void ToggleTheme() => ApplyTheme(EffectiveIsLight ? AppTheme.Dark : AppTheme.Light);

    /// <summary>Applies whatever was chosen last time. Called once, at start-up.</summary>
    public void ApplyStoredTheme() => ApplyTheme(CurrentTheme);

    private void Repaint()
    {
        Application? app = Application.Current;
        if (app is null)
        {
            // No application object: a unit test, or the designer. The choice is still recorded.
            ChangedColours = 0;
            return;
        }

        (ChangedColours, UnknownKeys) =
            InstallPalette(app.Resources, ThemePalette.For(EffectiveIsLight));

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _systemWatch?.Dispose();
        _systemWatch = null;
    }
}
