using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
    /// <summary>Palette cycled through as channels are discovered.</summary>
    private static readonly string[] Palette =
    {
        "#FF5555", "#50FA7B", "#8BE9FD", "#BD93F9", "#FFB86C",
        "#FF79C6", "#F1FA8C", "#66FCF1", "#00FF9D", "#FF2E63"
    };

    private readonly Queue<double> _timestamps;
    private readonly Queue<double> _values;
    private readonly int _capacity;
    private bool _isVisible = true;

    public ScopeChannelSeries(string name, int index, int capacity = 400)
    {
        Name = name;
        DisplayName = name;
        ColorHex = Palette[index % Palette.Length];
        Brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorHex));
        Brush.Freeze();

        _capacity = capacity;
        _timestamps = new Queue<double>(capacity);
        _values = new Queue<double>(capacity);
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
