using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using TelemetryDashboard.UI.ViewModels;

namespace TelemetryDashboard.UI.Controls;

/// <summary>
/// The scope's two measurement cursors and the delta readout between them.
/// </summary>
/// <remarks>
/// <see cref="DeltaCursorService"/> was written complete — bounds clamping, signed deltas, a
/// separate "both cursors placed" state so a half-finished gesture cannot read as a measurement —
/// and constructed by nothing but its own tests. The scope could show a transient and offered no
/// way to say how long it lasted or how far the rail dropped, which is the first question anyone
/// asks of a waveform.
/// </remarks>
public partial class ScopeViewControl
{
    private readonly DeltaCursorService _cursors = new();

    /// <summary>Which cursor the next click places. Alternates, so two clicks give a measurement.</summary>
    private bool _placeSecondCursor;

    private bool CursorsEnabled => BtnCursors.IsChecked == true;

    private void BtnCursors_Changed(object sender, RoutedEventArgs e)
    {
        CursorHud.Visibility = CursorsEnabled ? Visibility.Visible : Visibility.Collapsed;

        if (!CursorsEnabled)
        {
            // Cleared on the way out rather than kept. Leaving them placed means turning cursors
            // back on later shows a measurement taken from a trace that has since scrolled away.
            _cursors.ClearData();
            _placeSecondCursor = false;
        }

        UpdateCursorReadout();
        RedrawForCursors();
    }

    /// <summary>
    /// Repaints so a cursor appears, including while the scope is paused.
    /// </summary>
    /// <remarks>
    /// The render tick returns early when paused, and pausing is exactly when an operator measures
    /// — you freeze the transient, then put cursors on it. Setting the dirty flag alone meant a
    /// cursor placed on a frozen trace did not appear until the scope was resumed, which is the
    /// moment the thing being measured scrolls away.
    /// <para>
    /// Replotting while paused is safe because pause stops ingestion as well as drawing, so the
    /// figure is rebuilt from the same samples it already showed.
    /// </para>
    /// </remarks>
    private void RedrawForCursors()
    {
        if (_isPaused) ReplotData();
        else _needsRedraw = true;
    }

    /// <summary>Places a cursor where the operator clicked on the plot.</summary>
    /// <remarks>
    /// Bound to the plot's own mouse event rather than the control's, so a click on the toolbar
    /// cannot place a cursor at whatever coordinate that pixel happens to map to.
    /// </remarks>
    private void MainPlot_CursorClick(object sender, MouseButtonEventArgs e)
    {
        if (!CursorsEnabled) return;

        Point click = e.GetPosition(MainPlot);

        // WPF measures this control in device-independent units and the plotting library works in
        // real pixels. On a 150 % display those differ by half again, and a cursor placed without
        // the conversion lands somewhere the operator did not click.
        double scale = MainPlot.DisplayScale;
        var pixel = new ScottPlot.Pixel((float)(click.X * scale), (float)(click.Y * scale));
        ScottPlot.Coordinates point = MainPlot.Plot.GetCoordinates(pixel);

        ScottPlot.AxisLimits limits = MainPlot.Plot.Axes.GetLimits();
        _cursors.SetDataBounds(limits.Left, limits.Right);

        if (_placeSecondCursor) _cursors.SetCursor2(point.X, point.Y);
        else _cursors.SetCursor1(point.X, point.Y);

        _placeSecondCursor = !_placeSecondCursor;

        UpdateCursorReadout();
        RedrawForCursors();
    }

    /// <summary>Draws the cursors. Called after every replot, because the replot clears the figure.</summary>
    private void DrawCursors()
    {
        if (!CursorsEnabled || !_cursors.HasAnyCursor) return;

        AddCursorMarks(_cursors.Cursor1Time, _cursors.Cursor1Value);

        if (_cursors.HasValidMeasurement)
        {
            AddCursorMarks(_cursors.Cursor2Time, _cursors.Cursor2Value);
        }
    }

    /// <summary>
    /// One cursor: a vertical and a horizontal line crossing at the point.
    /// </summary>
    /// <remarks>
    /// Two crossed lines rather than a marker glyph, which is what a bench scope draws and what
    /// makes the reading checkable — the vertical says where on the time axis, the horizontal says
    /// where on the value axis, and both can be traced to the axis labels by eye.
    /// </remarks>
    private void AddCursorMarks(double x, double y)
    {
        ScottPlot.Color colour = PlotColor("AccentBrush");

        var vertical = MainPlot.Plot.Add.VerticalLine(x);
        vertical.Color = colour;
        vertical.LineWidth = 1;

        var horizontal = MainPlot.Plot.Add.HorizontalLine(y);
        horizontal.Color = colour;
        horizontal.LineWidth = 1;
    }

    /// <summary>
    /// Writes the delta, or says why there is not one yet.
    /// </summary>
    /// <remarks>
    /// The value delta is withheld while "Fit each" is on, and that is why this is a method rather
    /// than a format string. Under per-channel scaling the Y axis carries no units — every channel
    /// is mapped onto a common band — so a difference read off it is a number with no quantity
    /// behind it. Printing one there would be the same defect as the renormalised axis this scope
    /// already refuses to leave unlabelled.
    /// </remarks>
    private void UpdateCursorReadout()
    {
        if (!CursorsEnabled) return;

        if (!_cursors.HasValidMeasurement)
        {
            CursorDeltaText.Text = _placeSecondCursor
                ? "Now click the second point."
                : "Click the plot to place the first cursor.";
            CursorPointsText.Text = string.Empty;
            CursorNoteText.Visibility = Visibility.Collapsed;
            return;
        }

        string deltaTime = TelemetryDashboard.Core.Analytics.IntervalFormat.Seconds(_cursors.DeltaTime);

        if (_fitEachChannel)
        {
            CursorDeltaText.Text = "dt = " + deltaTime;
            CursorNoteText.Text = "dY withheld: 'Fit each' scales every channel onto a shared band, "
                                + "so the axis carries no unit to subtract.";
            CursorNoteText.Visibility = Visibility.Visible;
        }
        else
        {
            CursorDeltaText.Text = string.Create(CultureInfo.InvariantCulture,
                $"dt = {deltaTime}   dY = {_cursors.DeltaValue:G6}");
            CursorNoteText.Visibility = Visibility.Collapsed;
        }

        CursorPointsText.Text = string.Create(CultureInfo.InvariantCulture,
            $"1: ({_cursors.Cursor1Time:F3} s, {_cursors.Cursor1Value:G6})   "
            + $"2: ({_cursors.Cursor2Time:F3} s, {_cursors.Cursor2Value:G6})");
    }

}
