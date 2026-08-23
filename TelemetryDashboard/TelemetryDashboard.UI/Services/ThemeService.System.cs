using System.Windows;

namespace TelemetryDashboard.UI.Services;

public sealed partial class ThemeService
{
    /// <summary>Which of the two palettes a choice paints in, asking Windows if it has to.</summary>
    private static bool Resolve(AppTheme theme) => theme switch
    {
        AppTheme.Light => true,
        AppTheme.System => SystemTheme.IsLight(),
        _ => false
    };

    /// <summary>
    /// Windows changed while the operator had asked to follow it.
    /// </summary>
    /// <remarks>
    /// <c>SystemEvents</c> raises this on a thread of its own, and a resource dictionary belongs to
    /// the UI thread. Going through the dispatcher is not tidiness — touching a dictionary from
    /// there throws, and it would throw on somebody else's machine at whatever moment they happened
    /// to change their Windows theme, which is the hardest kind of fault to be told about.
    /// </remarks>
    private void OnSystemThemeChanged(bool light)
    {
        Application? app = Application.Current;
        if (app is null) { ApplyIfChanged(light); return; }

        app.Dispatcher.BeginInvoke(() => ApplyIfChanged(light));
    }

    /// <summary>
    /// Repaints only if this still is what the operator asked for, and only if it differs.
    /// </summary>
    /// <remarks>
    /// Both guards earn their place. The first: a notification already in flight when somebody
    /// picks Dark by hand must not arrive a moment later and overrule them. The second: Windows
    /// raises its General preference change for a great many things that are not the theme, and a
    /// repaint per event would put a line in the operator's log every time they moved a window.
    /// </remarks>
    private void ApplyIfChanged(bool light)
    {
        if (!FollowsSystem || light == EffectiveIsLight) return;

        EffectiveIsLight = light;
        Repaint();
    }
}
