using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;

namespace TelemetryDashboard.UI.Dialogs;

/// <summary>
/// Renders a node configuration as firmware source the operator can copy or save.
/// </summary>
/// <remarks>
/// The dialog used to build a <see cref="SensorNodeConfig"/> from literals — node id
/// <c>STM32_MCU_NODE_1</c>, three invented channels — and present the result as the configuration
/// of the attached hardware. It now takes the real configuration when one is supplied, and when
/// none is, says on screen that the code is a worked example and lists exactly which values went
/// into it.
/// </remarks>
public partial class CHeaderExportDialog : Window
{
    private readonly CHeaderGenerator _generator = new();
    private string _headerContent = string.Empty;
    private string _driverContent = string.Empty;

    /// <param name="config">
    /// The node configuration to generate from. Omitted until the application has one to hand over.
    /// </param>
    public CHeaderExportDialog(SensorNodeConfig? config = null)
    {
        InitializeComponent();
        GenerateCodes(config);
    }

    private void GenerateCodes(SensorNodeConfig? config)
    {
        bool isExample = config is null;
        config ??= ExampleConfig();

        TxtSourceNotice.Text = isExample
            ? "예시 설정으로 생성한 코드입니다. 연결된 노드의 설정에서 생성한 것이 아닙니다."
            : "현재 프로파일과 직렬 설정에서 생성한 코드입니다. 대상 플랫폼만 기본값입니다.";
        TxtSourceNotice.SetResourceReference(ForegroundProperty,
            isExample ? "WarningBrush" : "TextPrimaryBrush");

        string variables = config.Variables.Count == 0
            ? "채널 없음"
            : string.Join(", ", config.Variables.Select(v => $"{v.Name} ({v.DataType})"));

        TxtSourceSummary.Text =
            $"노드 {config.NodeId} · 대상 {config.TargetPlatform} · {config.BaudRate} baud · " +
            $"태그 {config.TagPrefix} · 버퍼 {config.BufferSize}바이트 · 채널: {variables}";

        _headerContent = _generator.GenerateHeader(config);
        _driverContent = _generator.GenerateDriverCode(config, config.TargetPlatform);

        TxtHeaderCode.Text = _headerContent;
        TxtDriverCode.Text = _driverContent;
    }

    /// <summary>
    /// A worked example, used only when no real configuration was supplied. The identifiers say
    /// what they are, so generated code cannot be mistaken for a description of live hardware.
    /// </summary>
    private static SensorNodeConfig ExampleConfig()
    {
        var config = new SensorNodeConfig
        {
            NodeId = "EXAMPLE_NODE",
            TagPrefix = "TELE",
            BaudRate = 115200,
            BufferSize = 128,
            TargetPlatform = "STM32"
        };
        config.Variables.Add(new VariableDefinition { Name = "temperature", DataType = "float" });
        config.Variables.Add(new VariableDefinition { Name = "vibration", DataType = "float" });
        config.Variables.Add(new VariableDefinition { Name = "rpm", DataType = "uint32_t" });
        return config;
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_headerContent + "\n\n" + _driverContent);
        MessageBox.Show(this, "헤더와 드라이버 코드를 클립보드에 복사했습니다.",
            "복사 완료", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog dlg = new SaveFileDialog
        {
            FileName = "telemetry_config.h",
            Filter = "C Header Files (*.h)|*.h|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        File.WriteAllText(dlg.FileName, _headerContent);
        string dir = Path.GetDirectoryName(dlg.FileName) ?? string.Empty;
        string driverPath = Path.Combine(dir, "telemetry_driver.c");
        File.WriteAllText(driverPath, _driverContent);

        MessageBox.Show(this, $"두 파일을 저장했습니다:\n{dlg.FileName}\n{driverPath}",
            "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Escape closes the dialog, as in every other dialog here.</summary>
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }
}
