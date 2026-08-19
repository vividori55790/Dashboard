using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;

namespace TelemetryDashboard.UI.Dialogs;

public partial class CHeaderExportDialog : Window
{
    private readonly CHeaderGenerator _generator = new();
    private string _headerContent = string.Empty;
    private string _driverContent = string.Empty;

    public CHeaderExportDialog()
    {
        InitializeComponent();
        GenerateCodes();
    }

    private void GenerateCodes()
    {
        SensorNodeConfig cfg = new SensorNodeConfig
        {
            NodeId = "STM32_MCU_NODE_1",
            TagPrefix = "TELE",
            BaudRate = 115200,
            BufferSize = 128,
            TargetPlatform = "STM32"
        };
        cfg.Variables.Add(new VariableDefinition { Name = "temperature", DataType = "float" });
        cfg.Variables.Add(new VariableDefinition { Name = "vibration", DataType = "float" });
        cfg.Variables.Add(new VariableDefinition { Name = "rpm", DataType = "uint32_t" });

        _headerContent = _generator.GenerateHeader(cfg);
        _driverContent = _generator.GenerateDriverCode("STM32");

        TxtHeaderCode.Text = _headerContent;
        TxtDriverCode.Text = _driverContent;
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_headerContent + "\n\n" + _driverContent);
        MessageBox.Show("C 헤더 및 드라이버 코드가 클립보드에 복사되었습니다.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog dlg = new SaveFileDialog
        {
            FileName = "telemetry_config.h",
            Filter = "C Header Files (*.h)|*.h|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            File.WriteAllText(dlg.FileName, _headerContent);
            string dir = Path.GetDirectoryName(dlg.FileName) ?? "";
            string driverPath = Path.Combine(dir, "telemetry_driver.c");
            File.WriteAllText(driverPath, _driverContent);

            MessageBox.Show($"파일이 성공적으로 저장되었습니다:\n- {dlg.FileName}\n- {driverPath}", "Saved Successfully", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
