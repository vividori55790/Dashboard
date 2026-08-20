using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TelemetryDashboard.UI.Controls;

public partial class DataTopologyOverlay : UserControl
{
    public event Action<string>? OnNodeSelected;

    /// <summary>The stage the operator last clicked, so its marker can be cleared.</summary>
    private Border? _selectedNode;

    public DataTopologyOverlay()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Reports what the pipeline is carrying: a description of the source, and the measured sample
    /// rate, or nothing when no rate has been measured yet.
    /// </summary>
    /// <remarks>
    /// The previous signature took a rate and an <c>isSimulating</c> flag, and its only caller
    /// passed the constants <c>50</c> and <c>true</c> on every tick — so the panel reported a
    /// simulated 50 Hz stream whatever the source was doing. A rate the caller has not measured is
    /// now expressed as no rate rather than as a plausible number.
    /// </remarks>
    public void UpdateTopologyStatus(string sourceInfo, double? samplesPerSecond)
    {
        Dispatcher.Invoke(() =>
        {
            SourceDetailsText.Text = sourceInfo;

            if (samplesPerSecond is double rate)
            {
                ThroughputText.Text = $"{rate:N0} samples/s, average over the run";
                SetLed("SuccessBrush");
            }
            else
            {
                ThroughputText.Text = "No data received yet";
                SetLed("TextTertiaryBrush");
            }
        });
    }

    /// <summary>Colours the flow indicator from a theme token.</summary>
    private void SetLed(string brushKey)
    {
        if (TryFindResource(brushKey) is Brush brush)
        {
            StatusLed.Fill = brush;
        }
    }

    /// <summary>
    /// Marks the clicked stage and tells whoever is listening which one it was.
    /// </summary>
    /// <remarks>
    /// The previous handler opened a modal dialog announcing that it highlighted the channels
    /// flowing through that stage. Nothing was highlighted and no channel was touched, so the only
    /// honest feedback available is that the stage is now the selected one — which is what it does.
    /// </remarks>
    private void Node_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Name is not string name) return;

        _selectedNode?.ClearValue(Border.BorderBrushProperty);
        _selectedNode = border;

        if (TryFindResource("AccentBrush") is Brush accent)
        {
            border.BorderBrush = accent;
        }

        OnNodeSelected?.Invoke(name);
    }
}
