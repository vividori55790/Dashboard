using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using TelemetryDashboard.Core.Plugins;

namespace TelemetryDashboard.UI.Dialogs;

/// <summary>
/// Checks a derived-signal expression, and evaluates it when live channel values are available.
/// </summary>
/// <remarks>
/// The evaluation used to run against a hard-coded table — Voltage 220.5, Current 14.8, anything
/// else 10.0 — and print the arithmetic under the heading "live formula evaluation output". Every
/// number on screen was invented by the dialog. With no value source wired the dialog now reports
/// the syntax check and withholds the result, naming the values it could not obtain.
/// </remarks>
public partial class FormulaEvaluatorDialog : Window
{
    private const int MaxHistoryLines = 50;

    private readonly FormulaEvaluator _evaluator = new();
    private readonly Func<string, string, double?>? _channelValue;
    private readonly List<string> _unresolved = new();

    /// <param name="channelValue">
    /// Resolves a node/variable pair to its current reading, or null when that channel has no
    /// value. Omitted until the application passes its live channel store.
    /// </param>
    public FormulaEvaluatorDialog(Func<string, string, double?>? channelValue = null)
    {
        InitializeComponent();
        _channelValue = channelValue;

        TxtValueSource.Text = _channelValue is null
            ? "연결된 채널 값이 없어 문법만 확인합니다. 계산 결과는 표시하지 않습니다."
            : "수신 중인 채널 값으로 계산합니다.";

        RunEvaluation();
    }

    private void RunEvaluation()
    {
        string expr = TxtExpression.Text.Trim();
        if (string.IsNullOrEmpty(expr)) return;

        string varName = "result";
        string formulaBody = expr;

        if (expr.Contains('='))
        {
            var parts = expr.Split('=', 2);
            varName = parts[0].Trim();
            formulaBody = parts[1].Trim();
        }

        try
        {
            _unresolved.Clear();
            double value = _evaluator.Evaluate(formulaBody, string.Empty, Resolve);

            // A result computed from probe values is not a measurement, so it is not shown. The
            // syntax check is real either way, and that is what the line reports.
            AddLine(_unresolved.Count == 0
                ? $"{varName} = {value:F4}"
                : $"{varName} = -  ·  문법 정상, 값 없음: {string.Join(", ", _unresolved)}");
        }
        catch (Exception ex)
        {
            AddLine($"오류: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the live reading, or records the variable as unavailable and hands back a probe
    /// value so the rest of the expression still gets parsed and type-checked.
    /// </summary>
    private double Resolve(string nodeId, string variable)
    {
        double? live = _channelValue?.Invoke(nodeId, variable);
        if (live.HasValue) return live.Value;

        _unresolved.Add(string.IsNullOrEmpty(nodeId) ? variable : $"[{nodeId}].{variable}");
        return 1.0;
    }

    private void AddLine(string text)
    {
        LstEvalResults.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss.fff}] {text}");

        while (LstEvalResults.Items.Count > MaxHistoryLines)
        {
            LstEvalResults.Items.RemoveAt(LstEvalResults.Items.Count - 1);
        }
    }

    private void BtnEvaluate_Click(object sender, RoutedEventArgs e) => RunEvaluation();

    private void TxtExpression_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        // Marking it handled keeps the default button from evaluating the same expression twice.
        e.Handled = true;
        RunEvaluation();
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
