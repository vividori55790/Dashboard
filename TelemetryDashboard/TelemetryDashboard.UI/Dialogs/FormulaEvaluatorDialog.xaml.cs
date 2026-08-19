using System;
using System.Windows;
using System.Windows.Input;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Plugins;

namespace TelemetryDashboard.UI.Dialogs;

public partial class FormulaEvaluatorDialog : Window
{
    private readonly FormulaEvaluator _evaluator = new();

    public FormulaEvaluatorDialog()
    {
        InitializeComponent();
        RunEvaluation();
    }

    private void RunEvaluation()
    {
        string expr = TxtExpression.Text.Trim();
        if (string.IsNullOrEmpty(expr)) return;

        try
        {
            string varName = "result";
            string formulaBody = expr;

            if (expr.Contains('='))
            {
                var parts = expr.Split('=', 2);
                varName = parts[0].Trim();
                formulaBody = parts[1].Trim();
            }

            double evaluatedVal = _evaluator.Evaluate(formulaBody, "COM3", (nodeId, var) =>
            {
                if (var.Equals("Voltage", StringComparison.OrdinalIgnoreCase)) return 220.5;
                if (var.Equals("Current", StringComparison.OrdinalIgnoreCase)) return 14.8;
                if (var.Equals("Temp_C", StringComparison.OrdinalIgnoreCase)) return 25.0;
                if (var.Equals("X", StringComparison.OrdinalIgnoreCase)) return 0.3;
                if (var.Equals("Y", StringComparison.OrdinalIgnoreCase)) return 0.4;
                return 10.0;
            });

            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            LstEvalResults.Items.Insert(0, $"[{timestamp}] EXPR: {expr} => {varName} = {evaluatedVal:F4}");

            if (LstEvalResults.Items.Count > 50)
            {
                LstEvalResults.Items.RemoveAt(LstEvalResults.Items.Count - 1);
            }
        }
        catch (Exception ex)
        {
            LstEvalResults.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}");
        }
    }

    private void BtnEvaluate_Click(object sender, RoutedEventArgs e)
    {
        RunEvaluation();
    }

    private void TxtExpression_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RunEvaluation();
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
