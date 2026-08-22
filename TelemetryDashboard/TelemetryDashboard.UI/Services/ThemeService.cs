using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using TelemetryDashboard.UI.Themes;

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

/// <summary>
/// Applies a palette to the running application, and remembers which one.
/// </summary>
/// <remarks>
/// What this did before was reflect for <c>Wpf.Ui.Appearance.ApplicationThemeManager</c>, call
/// <c>GetMethod("Apply")</c>, and invoke it with <c>null</c> arguments — so the theme the operator
/// chose was never passed to anything. It could not have worked in three separate ways: the
/// argument was dropped, the ambiguous overload made <c>GetMethod</c> throw into a bare
/// <c>catch</c>, and no XAML in this application references the Wpf.Ui namespace at all, so there
/// was nothing for that library to restyle. The log line said "Theme toggled." each time.
/// </remarks>
public class ThemeService
{
    private readonly UiSettings _settings;

    public ThemeService(UiSettings? settings = null)
    {
        _settings = settings ?? UiSettings.Load();
        CurrentTheme = Enum.TryParse(_settings.Theme, ignoreCase: true, out AppTheme stored)
            ? stored
            : AppTheme.Dark;
    }

    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    /// <summary>Brush keys the palette named that the resource dictionary does not have.</summary>
    /// <remarks>Reported rather than thrown: a mistyped key must not stop the window opening.</remarks>
    public IReadOnlyList<string> UnknownKeys { get; private set; } = Array.Empty<string>();

    /// <summary>Brushes actually repainted by the last apply.</summary>
    public int RepaintedBrushes { get; private set; }
    public bool EnableMica { get; set; } = true;
    public WindowBackdropType BackdropType { get; set; } = WindowBackdropType.Mica;
    public double CurrentDpiScale { get; private set; } = 1.0;

    public bool IsMicaSupportedOnCurrentOS()
    {
        return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
    }

    /// <summary>
    /// Repaints every themed brush, and stores the choice for the next launch.
    /// </summary>
    /// <remarks>
    /// The brushes are mutated in place rather than replaced. Every consumer refers to them with
    /// <c>StaticResource</c>, which resolves once and never looks again — but all of them hold the
    /// same object, and none of the brushes is frozen, so setting <c>Color</c> is seen everywhere
    /// at once. Swapping the dictionary instead would have changed nothing already rendered.
    /// </remarks>
    public void ApplyTheme(AppTheme theme)
    {
        CurrentTheme = theme;

        _settings.Theme = theme.ToString();
        _settings.Save();

        Application? app = Application.Current;
        if (app is null)
        {
            // No application object: a unit test, or the designer. The choice is still recorded.
            RepaintedBrushes = 0;
            return;
        }

        (int repainted, int replaced, IReadOnlyList<string> unknown) =
            InstallPalette(app.Resources, ThemePalette.For(theme == AppTheme.Light));

        RepaintedBrushes = repainted;
        UnknownKeys = unknown;

        // Anything that had to be replaced rather than repainted is invisible to what is already
        // drawn, because StaticResource kept the old object. Saying so is the difference between a
        // feature and the "Theme toggled." message this replaces.
        NeedsRestartToShow = replaced > 0;
    }

    /// <summary>True when the chosen theme will only be visible after a restart.</summary>
    public bool NeedsRestartToShow { get; private set; }


    /// <summary>
    /// Puts a palette's colours behind the token brushes.
    /// </summary>
    /// <remarks>
    /// Two ways, because WPF gives two situations. A brush that is not frozen is repainted in
    /// place, and every <c>StaticResource</c> holding that object sees it — 900 references reached
    /// without touching any markup. A brush that <em>is</em> frozen cannot be repainted, so the
    /// dictionary entry is replaced instead; that reaches everything resolved after the swap and
    /// nothing resolved before it.
    /// <para>
    /// Which case applies is not a detail: WPF freezes Freezables in dictionaries loaded from
    /// compiled BAML, so in the running application every one of these is frozen, and only the
    /// replacement path runs. Called before the first window exists, that is enough — nothing has
    /// resolved anything yet, so the whole interface comes up in the chosen palette. Called while
    /// the window is open, it changes nothing already on screen, and the caller has to say so.
    /// </para>
    /// <para>
    /// The first version of this repainted in place only. It passed its tests, because those load
    /// Tokens.xaml through XamlReader and XamlReader does not freeze, and it reported
    /// "Light theme applied to 0 brushes" the first time it ran in the real application.
    /// </para>
    /// </remarks>
    /// <returns>How many were repainted in place, and how many had to be replaced.</returns>
    public static (int Repainted, int Replaced, IReadOnlyList<string> Unknown) InstallPalette(
        ResourceDictionary resources, IReadOnlyDictionary<string, Color> palette)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(palette);

        var missing = new List<string>();
        int repainted = 0, replaced = 0;

        foreach ((string key, Color colour) in palette)
        {
            if (resources[key] is not SolidColorBrush brush) { missing.Add(key); continue; }

            if (brush.IsFrozen)
            {
                resources[key] = new SolidColorBrush(colour);
                replaced++;
            }
            else
            {
                brush.Color = colour;
                repainted++;
            }
        }

        return (repainted, replaced, missing);
    }

    /// <summary>
    /// Installs the stored palette. Must run before the first window is created.
    /// </summary>
    /// <returns>How many brushes were set.</returns>
    public static int InstallStoredPaletteAtStartup(ResourceDictionary resources, UiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        bool light = string.Equals(settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        (int repainted, int replaced, _) = InstallPalette(resources, ThemePalette.For(light));
        return repainted + replaced;
    }

    /// <summary>Applies whatever was chosen last time. Called once, at start-up.</summary>
    public void ApplyStoredTheme() => ApplyTheme(CurrentTheme);

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
