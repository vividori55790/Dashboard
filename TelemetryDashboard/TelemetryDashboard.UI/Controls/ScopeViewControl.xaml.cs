using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TelemetryDashboard.UI.Controls;

/// <summary>
/// Real-time multi-channel oscilloscope view.
/// </summary>
/// <remarks>
/// Channels are discovered from the incoming stream, so any telemetry source plots without the
/// control knowing its schema in advance. Samples are batched at the render tick rather than
/// redrawn per packet, which keeps the window responsive during a burst.
/// </remarks>
public partial class ScopeViewControl : UserControl
{
    /// <summary>Maximum channels charted simultaneously before new ones are ignored.</summary>
    private const int MaxChannels = 16;

    /// <summary>Backlog cap; excess is dropped oldest-first during a flood.</summary>
    private const int MaxPendingSamples = 4000;

    private readonly ConcurrentQueue<(string Channel, double Value)> _pending = new();
    private readonly ObservableCollection<ScopeChannelSeries> _channels = new();
    private readonly Dictionary<string, ScopeChannelSeries> _channelIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _batchTimer;

    private bool _isPaused;
    private long _sampleCount;
    private DateTime _startTime = DateTime.Now;

    public ScopeViewControl()
    {
        InitializeComponent();
        InitializePlot();

        ChannelToggles.ItemsSource = _channels;

        // ~60 Hz render batching so a 50,000 pkt/s burst cannot flood the dispatcher.
        _batchTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _batchTimer.Tick += (_, _) => ProcessPendingBatch();
        _batchTimer.Start();
    }

    /// <summary>Channels discovered so far.</summary>
    public IReadOnlyList<ScopeChannelSeries> Channels => _channels;

    /// <summary>
    /// Applies the application's tokens to the plotting library.
    /// </summary>
    /// <remarks>
    /// The plot used to set only its two background colours, from a literal hex that happened to
    /// match the window at the time, and left axis text at the library's default near-black — so
    /// the tick labels and axis titles were drawn nearly invisibly on a dark figure. Colours here
    /// are read from the theme dictionary, so the chart follows the palette instead of shadowing it.
    /// </remarks>
    private void InitializePlot()
    {
        MainPlot.Plot.FigureBackground = PlotColor("CanvasBrush");
        MainPlot.Plot.DataBackground = PlotColor("InsetBrush");
        MainPlot.Plot.Style.ColorAxes(PlotColor("TextSecondaryBrush"));
        MainPlot.Plot.Style.ColorGrids(PlotColor("GridLineBrush"));

        // ColorAxes paints the axis frame the same colour as the tick labels, and text contrast is
        // not border contrast: at that weight the frame drew a bright rectangle around the plot —
        // the "white border" the chart appeared to have. The frame is a container edge, so it takes
        // the border token every other container edge in the application takes.
        foreach (ScottPlot.IAxis axis in MainPlot.Plot.Axes.GetAxes())
        {
            axis.FrameLineStyle.Color = PlotColor("BorderDefaultBrush");
        }

        MainPlot.Plot.Title("Live telemetry");
        MainPlot.Plot.XLabel("Time (s)");
        MainPlot.Plot.YLabel("Value");
        MainPlot.Refresh();
    }

    /// <summary>Converts a theme brush into the plotting library's colour type.</summary>
    private ScottPlot.Color PlotColor(string brushKey) =>
        TryFindResource(brushKey) is System.Windows.Media.SolidColorBrush brush
            ? new ScottPlot.Color(brush.Color.R, brush.Color.G, brush.Color.B, brush.Color.A)
            : ScottPlot.Color.Gray(128);

    /// <summary>Queues one sample for a named channel, creating the channel on first sight.</summary>
    public void PushChannel(string channelName, double value)
    {
        if (_isPaused || string.IsNullOrWhiteSpace(channelName)) return;
        if (double.IsNaN(value) || double.IsInfinity(value)) return;

        _pending.Enqueue((channelName, value));

        while (_pending.Count > MaxPendingSamples)
        {
            _pending.TryDequeue(out _);
        }
    }

    /// <summary>
    /// Convenience overload for the bundled ambient sensor set.
    /// </summary>
    public void PushTelemetryData(double temp, double hum, double vib, double rpm)
    {
        PushChannel("Temperature", temp);
        PushChannel("Humidity", hum);
        PushChannel("Vibration", vib);
        PushChannel("RPM", rpm);
    }

    public void ProcessPendingBatch()
    {
        if (_isPaused || _pending.IsEmpty) return;

        double elapsedSec = (DateTime.Now - _startTime).TotalSeconds;
        bool channelAdded = false;

        while (_pending.TryDequeue(out (string Channel, double Value) sample))
        {
            ScopeChannelSeries? series = Resolve(sample.Channel, ref channelAdded);
            if (series is null) continue;

            series.Add(elapsedSec, sample.Value);
            _sampleCount++;
        }

        ReplotData();
        ScopeStatsText.Text = $"Samples: {_sampleCount:N0} | Channels: {_channels.Count} | Time: {elapsedSec:F1}s";

        // Measured, not assumed. The overlay was previously told "50 Hz, simulating" on every tick
        // regardless of the source or the actual throughput.
        double? rate = elapsedSec > 0 ? _sampleCount / elapsedSec : null;
        TopologyOverlay.UpdateTopologyStatus(
            $"{_channels.Count} channel(s) discovered", rate);
    }

    /// <summary>Finds or creates the series for a channel, honouring the channel cap.</summary>
    private ScopeChannelSeries? Resolve(string channelName, ref bool channelAdded)
    {
        if (_channelIndex.TryGetValue(channelName, out ScopeChannelSeries? existing)) return existing;
        if (_channels.Count >= MaxChannels) return null;

        var series = new ScopeChannelSeries(channelName, _channels.Count);
        _channelIndex[channelName] = series;
        _channels.Add(series);
        channelAdded = true;
        return series;
    }

    private void ReplotData()
    {
        MainPlot.Plot.Clear();

        foreach (ScopeChannelSeries channel in _channels)
        {
            if (!channel.IsVisible || channel.SampleCount < 2) continue;

            (double[] xs, double[] ys) = channel.Snapshot();
            var scatter = MainPlot.Plot.Add.Scatter(xs, ys);
            scatter.Color = ScottPlot.Color.FromHex(channel.ColorHex);
            scatter.Label = channel.Name;
        }

        MainPlot.Plot.Axes.AutoScale();
        MainPlot.Refresh();
    }

    private void BtnPause_Click(object sender, RoutedEventArgs e)
    {
        _isPaused = !_isPaused;

        // Glyph and caption are separate elements so the icon font is never asked to render a
        // caption, and the caption is never asked to render an icon.
        PauseGlyph.Text = _isPaused ? "\uE768" : "\uE769";
        PauseLabel.Text = _isPaused ? "Resume" : "Pause";
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        _pending.Clear();
        foreach (ScopeChannelSeries channel in _channels) channel.Clear();
        _channels.Clear();
        _channelIndex.Clear();
        _sampleCount = 0;
        _startTime = DateTime.Now;
        ReplotData();
        ScopeStatsText.Text = "Samples: 0 | Channels: 0";
        TopologyOverlay.UpdateTopologyStatus("No channels yet", null);
    }

    private void BtnAutoFit_Click(object sender, RoutedEventArgs e)
    {
        MainPlot.Plot.Axes.AutoScale();
        MainPlot.Refresh();
    }

    private void Channel_CheckChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) ReplotData();
    }
}
