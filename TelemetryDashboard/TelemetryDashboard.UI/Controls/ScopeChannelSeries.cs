using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace TelemetryDashboard.UI.Controls;

/// <summary>
/// One plotted channel: its rolling sample window, colour, and visibility toggle.
/// </summary>
/// <remarks>
/// Channels are created on first sight rather than declared up front. The scope previously held
/// four fixed lists named temp/hum/vib/rpm, so a deployment reporting bus voltage or torque had
/// nowhere to draw — the chart silently showed nothing while the data flowed past it.
/// </remarks>
public sealed class ScopeChannelSeries : INotifyPropertyChanged
{
    /// <summary>
    /// Palette cycled through as channels are discovered, named by theme key rather than by value.
    /// </summary>
    /// <remarks>
    /// This used to be ten literal hex codes of its own — a second palette competing with the
    /// application's, including a green and a red that read as "healthy" and "alarming" beside
    /// controls where those colours mean exactly that. The series tokens are chosen to stay
    /// distinguishable under the common forms of colour blindness, which an ad-hoc list is not.
    /// </remarks>
    private static readonly string[] PaletteKeys =
    {
        "Series1Brush", "Series2Brush", "Series3Brush", "Series4Brush", "Series5Brush"
    };

    private readonly Queue<double> _timestamps;
    private readonly Queue<double> _values;
    private readonly int _capacity;
    private bool _isVisible = true;

    public ScopeChannelSeries(string name, int index, int capacity = 400)
    {
        Name = name;
        DisplayName = name;

        Color colour = PaletteColour(index);
        ColorHex = $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";
        Brush = new SolidColorBrush(colour);
        Brush.Freeze();

        _capacity = capacity;
        _timestamps = new Queue<double>(capacity);
        _values = new Queue<double>(capacity);
    }

    /// <summary>
    /// Reads the series colour for a channel index out of the application's theme dictionary.
    /// </summary>
    /// <remarks>
    /// Falls back to a neutral grey only when there is no running application to ask — a unit test
    /// or the designer. Inventing a colour there would put a value back in the code that the theme
    /// is supposed to own.
    /// </remarks>
    private static Color PaletteColour(int index)
    {
        string key = PaletteKeys[Math.Abs(index) % PaletteKeys.Length];
        return Application.Current?.TryFindResource(key) is SolidColorBrush themed
            ? themed.Color
            : Colors.Gray;
    }

    public string Name { get; }

    public string DisplayName { get; }

    public string ColorHex { get; }

    public Brush Brush { get; }

    public int SampleCount => _values.Count;

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            OnPropertyChanged();
        }
    }

    public void Add(double timestampSec, double value)
    {
        _timestamps.Enqueue(timestampSec);
        _values.Enqueue(value);

        while (_values.Count > _capacity)
        {
            _timestamps.Dequeue();
            _values.Dequeue();
        }
    }

    /// <summary>Copies the window into arrays for the plotting library.</summary>
    public (double[] Xs, double[] Ys) Snapshot() => (_timestamps.ToArray(), _values.ToArray());

    public void Clear()
    {
        _timestamps.Clear();
        _values.Clear();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
