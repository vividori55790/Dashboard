using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using TelemetryDashboard.UI.ViewModels;

namespace TelemetryDashboard.UI.Controls;

/// <summary>
/// The digital-twin viewport: the machine's own geometry, or a placeholder that says why not.
/// </summary>
/// <remarks>
/// This control existed in full — toolbar, viewport, lights, grid — and was instantiated by
/// nothing. It had no window in the shell, so it had never once appeared in the running
/// application, while a <c>Twin3DMode</c> layout preset offered to arrange the workspace around it.
/// <para>
/// The mesh it drew was a box hard-coded in its own markup. <see cref="Twin3DService"/>,
/// <see cref="StlFileProbe"/> and <see cref="MeshBounds"/> — which between them decide whether a
/// file is really a mesh, how large it is, and what scale brings it inside the camera frustum —
/// were complete, tested, and constructed by nothing either. A twin that can only ever be a box is
/// a decoration; the point of the panel is to look at the machine you actually have.
/// </para>
/// </remarks>
public partial class DigitalTwin3DViewControl : UserControl
{
    /// <summary>
    /// Model loaded at start-up when it sits beside the executable. A convention rather than a
    /// setting, and the readout names it when it is missing so the convention is discoverable.
    /// </summary>
    public const string DefaultModelFileName = "twin-model.stl";

    private readonly Twin3DService _twin = new();
    private bool _started;

    public DigitalTwin3DViewControl()
    {
        InitializeComponent();

        // The DynamicResource a root element cannot spell in its own markup: XAML resolves a bare
        // attribute on the root against the WPF namespace, not against this class. Same mechanism,
        // written where it can be read.
        SetResourceReference(GridColorProperty, "GridLineColor");
        SetResourceReference(MeshColorProperty, "AccentColor");

        Loaded += OnLoaded;
    }

    /// <summary>Raised for the event log: one line per model decision.</summary>
    public event Action<string>? Notice;

    /// <summary>What the readout is showing, so a caller can log or assert on it.</summary>
    public string ModelSummary => ModelText.Text;

    /// <summary>Whether the viewport is showing a real mesh rather than the placeholder.</summary>
    public bool HasLoadedModel => _twin.HasModel && !_twin.IsFallbackModelActive;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded fires again every time the panel is re-docked or floated, and reloading the mesh
        // each time would throw away a camera the operator had just set up.
        if (_started) return;
        _started = true;

        string beside = Path.Combine(AppContext.BaseDirectory, DefaultModelFileName);
        if (File.Exists(beside)) LoadModel(beside);
        else ShowPlaceholder($"no {DefaultModelFileName} beside the executable");
    }

    /// <summary>
    /// Puts <paramref name="path"/> in the viewport, or the placeholder and a reason.
    /// </summary>
    /// <remarks>
    /// Two checks, not one. The probe asks whether the file is shaped like an STL; reading asks
    /// whether the mesh inside it exists. Files pass the first and fail the second often enough to
    /// matter, which is what <see cref="Twin3DService.RejectLoadedModel"/> and
    /// <see cref="TwinModelLoader"/> both carry the account of.
    /// </remarks>
    public bool LoadModel(string path)
    {
        if (!_twin.LoadModel(path))
        {
            ShowPlaceholder($"{Describe(path)} is not a mesh this can read");
            return false;
        }

        TwinModelLoad load = TwinModelLoader.Read(path);
        if (!load.Succeeded)
        {
            _twin.RejectLoadedModel();
            ShowPlaceholder($"{Describe(path)} {load.Failure}");
            return false;
        }

        _twin.SetCustomMesh(load.Vertices, load.Indices);

        double scale = _twin.ModelScale;
        ModelHost.Children.Clear();
        ModelHost.Children.Add(new ModelVisual3D
        {
            Content = load.Scene,
            Transform = new ScaleTransform3D(scale, scale, scale)
        });

        Viewport3D.ZoomExtents();
        Announce($"{Describe(path)} · {_twin.TriangleCount:N0} triangles · "
            + $"{scale.ToString("0.####", CultureInfo.InvariantCulture)}x to fit");
        return true;
    }

    /// <summary>Shows the placeholder box and says why it is there.</summary>
    public void ShowPlaceholder(string reason)
    {
        ModelHost.Children.Clear();
        ModelHost.Children.Add(new BoxVisual3D
        {
            Width = 6,
            Length = 4,
            Height = 1,
            Fill = FrozenBrush(MeshColor)
        });

        Viewport3D.ZoomExtents();
        Announce($"기본 형상 — {reason}");
    }

    private void Announce(string summary)
    {
        ModelText.Text = summary;
        Notice?.Invoke(summary);
    }

    /// <summary>File name alone: the readout sits in a toolbar and a full path pushes it off.</summary>
    private static string Describe(string path) =>
        string.IsNullOrWhiteSpace(path) ? "(no file)" : Path.GetFileName(path);

    private void BtnLoadModel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "디지털 트윈 모델 선택",
            Filter = "STL mesh (*.stl)|*.stl|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true) LoadModel(dialog.FileName);
    }

    private void BtnUsePlaceholder_Click(object sender, RoutedEventArgs e) =>
        ShowPlaceholder("operator asked for it");

    private void BtnResetView_Click(object sender, RoutedEventArgs e) => Viewport3D.ZoomExtents();

    private void BtnToggleGrid_Click(object sender, RoutedEventArgs e)
    {
        if (Viewport3D.Children.Contains(GridLines)) Viewport3D.Children.Remove(GridLines);
        else Viewport3D.Children.Add(GridLines);
    }
}
