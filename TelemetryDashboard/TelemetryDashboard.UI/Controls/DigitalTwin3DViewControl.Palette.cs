using System.Linq;
using System.Windows;
using System.Windows.Media;
using HelixToolkit.Wpf;

namespace TelemetryDashboard.UI.Controls;

public partial class DigitalTwin3DViewControl
{
    /// <summary>The token colour the grid lines are drawn in.</summary>
    public static readonly DependencyProperty GridColorProperty = DependencyProperty.Register(
        nameof(GridColor), typeof(Color), typeof(DigitalTwin3DViewControl),
        new PropertyMetadata(Colors.Gray, OnPaletteColourChanged));

    /// <summary>The token colour the placeholder mesh is drawn in.</summary>
    public static readonly DependencyProperty MeshColorProperty = DependencyProperty.Register(
        nameof(MeshColor), typeof(Color), typeof(DigitalTwin3DViewControl),
        new PropertyMetadata(Colors.SteelBlue, OnPaletteColourChanged));

    public Color GridColor
    {
        get => (Color)GetValue(GridColorProperty);
        set => SetValue(GridColorProperty, value);
    }

    public Color MeshColor
    {
        get => (Color)GetValue(MeshColorProperty);
        set => SetValue(MeshColorProperty, value);
    }

    /// <summary>
    /// Takes the palette by colour, because a 3D material will not accept a live brush.
    /// </summary>
    /// <remarks>
    /// This viewport used to bind <c>Fill</c> straight at the token brushes, and the moment those
    /// brushes became mutable so a theme change could reach them, the application stopped starting
    /// at all: HelixToolkit turns a fill into a <c>Material</c> and freezes it, a Freezable holding
    /// an unresolved expression cannot be frozen, and the constructor threw through the XAML
    /// parser before the first window appeared.
    /// <para>
    /// It is not a WPF quirk to work around. A 2D brush in this application is a handle whose
    /// colour changes underneath every control holding it; a 3D material is baked once and rendered
    /// by the graphics pipeline, which is why it insists on being frozen. So the viewport subscribes
    /// to the <em>colour</em> instead — the same token, by <c>DynamicResource</c> — and mints its
    /// own frozen brush each time that colour changes. The twin follows the theme, and nothing is
    /// asked to be two things at once.
    /// </para>
    /// </remarks>
    private static void OnPaletteColourChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((DigitalTwin3DViewControl)d).RepaintFromPalette();

    private void RepaintFromPalette()
    {
        // The first callback arrives while the markup is still being parsed, before either of these
        // fields has been assigned.
        if (GridLines is null || ModelHost is null) return;

        GridLines.Fill = FrozenBrush(GridColor);

        foreach (BoxVisual3D box in ModelHost.Children.OfType<BoxVisual3D>())
        {
            box.Fill = FrozenBrush(MeshColor);
        }
    }

    /// <summary>A brush a 3D material can accept: its own object, and finished changing.</summary>
    private static SolidColorBrush FrozenBrush(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }
}
