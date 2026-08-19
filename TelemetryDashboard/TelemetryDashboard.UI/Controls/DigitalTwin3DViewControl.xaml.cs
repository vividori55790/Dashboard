using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;

namespace TelemetryDashboard.UI.Controls;

public partial class DigitalTwin3DViewControl : UserControl
{
    private double _currentRoll = 0;
    private double _currentPitch = 0;
    private double _currentYaw = 0;

    public DigitalTwin3DViewControl()
    {
        InitializeComponent();
    }

    public void UpdateOrientation(double roll, double pitch, double yaw)
    {
        Dispatcher.Invoke(() =>
        {
            _currentRoll = roll;
            _currentPitch = pitch;
            _currentYaw = yaw;

            Transform3DGroup transformGroup = new();
            transformGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), roll)));
            transformGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), pitch)));
            transformGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), yaw)));

            TwinBox.Transform = transformGroup;
            OrientationText.Text = $"Roll: {roll:F1}° | Pitch: {pitch:F1}° | Yaw: {yaw:F1}°";
        });
    }

    private void BtnResetView_Click(object sender, RoutedEventArgs e)
    {
        Viewport3D.ResetCamera();
        UpdateOrientation(0, 0, 0);
    }

    private void BtnToggleGrid_Click(object sender, RoutedEventArgs e)
    {
        if (Viewport3D.Children.Contains(GridLines))
        {
            Viewport3D.Children.Remove(GridLines);
        }
        else
        {
            Viewport3D.Children.Add(GridLines);
        }
    }
}
