using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using TelemetryDashboard.Core.Analytics;

namespace TelemetryDashboard.UI.Dialogs;

/// <summary>
/// Detector settings and the anomalies the running analytics engine has actually recorded.
/// </summary>
/// <remarks>
/// The dialog reads the engine and never feeds it. It previously called
/// <see cref="TelemetryMlAnalyticsEngine.AnalyzeChannel"/> with four invented channel names and
/// randomised values, which did two separate kinds of damage: the z-scores, means and forecasts on
/// screen described telemetry that had never existed, and — because the engine passed in by
/// <c>MainWindow</c> is the live one — the invented samples entered the rolling windows backing
/// every other anomaly verdict in the application. Nothing here writes to the engine except
/// <see cref="TelemetryMlAnalyticsEngine.ZScoreThreshold"/>, which is a setting the operator is
/// deliberately changing.
/// </remarks>
public partial class MlAnalyticsDialog : Window
{
    /// <summary>Shown in place of a figure the system has not computed.</summary>
    private const string NoData = "—";

    private readonly TelemetryMlAnalyticsEngine _mlEngine;

    public MlAnalyticsDialog(TelemetryMlAnalyticsEngine mlEngine)
    {
        InitializeComponent();
        _mlEngine = mlEngine ?? throw new ArgumentNullException(nameof(mlEngine));

        SldThreshold.Value = _mlEngine.ZScoreThreshold;
        TxtThresholdVal.Text = FormatSigma(_mlEngine.ZScoreThreshold);

        ShowRecordedAnomalies();
    }

    /// <summary>
    /// Renders the engine's retained anomaly set, or states that it is empty.
    /// </summary>
    /// <remarks>
    /// <see cref="TelemetryMlAnalyticsEngine.RecentAnomalies"/> holds only channels that crossed the
    /// threshold, so an empty list means "nothing has been flagged", not "everything is healthy" —
    /// a distinction the empty-state text has to make, because the previous screen closed it by
    /// manufacturing four reassuring rows.
    /// </remarks>
    private void ShowRecordedAnomalies()
    {
        LstAnomalyScores.Items.Clear();
        LstForecasts.Items.Clear();

        TxtEngineInfo.Text = DescribeEngine();

        IReadOnlyList<AnomalyResult> anomalies = _mlEngine.RecentAnomalies;

        if (anomalies.Count == 0)
        {
            LstAnomalyScores.Items.Add(
                "기록된 이상치 없음 — 아직 임계값을 넘은 채널이 없거나 수집이 시작되지 않았습니다.");
            LstForecasts.Items.Add("표시할 외삽값 없음 (no recorded result to extrapolate from).");
            TxtRecordedAt.Text = $"엔진이 추적 중인 채널: {_mlEngine.TrackedChannelCount}개 · 기록된 이상치: 0개";
            return;
        }

        foreach (AnomalyResult result in anomalies)
        {
            LstAnomalyScores.Items.Add(DescribeScore(result));
            LstForecasts.Items.Add(DescribeForecast(result));
        }

        TxtRecordedAt.Text =
            $"엔진이 추적 중인 채널: {_mlEngine.TrackedChannelCount}개 · 기록된 이상치: {anomalies.Count}개 · " +
            $"조회 시각 {DateTime.Now:HH:mm:ss}";
    }

    /// <summary>Names the analyzer and the settings that produced every verdict shown.</summary>
    private string DescribeEngine() =>
        $"분석기 {_mlEngine.AnalyzerId} · 표본율 {_mlEngine.SampleRateHz:0.#} Hz · " +
        $"판정 최소 표본 {_mlEngine.MinimumSamples}개. " +
        "임계값 변경은 이후 수신되는 표본부터 적용되며, 이미 기록된 판정은 다시 계산되지 않습니다.";

    /// <summary>
    /// One recorded verdict. A result without an analyzer id is reported as unevaluated rather than
    /// as a confident zero, which is how the warm-up window used to read.
    /// </summary>
    private static string DescribeScore(AnomalyResult result)
    {
        if (!result.HasVerdict)
        {
            return $"[{result.ChannelName}] 값 {Fixed(result.CurrentValue)} · z-score {NoData} · 판정 없음 (not evaluated)";
        }

        return $"[{result.ChannelName}] 값 {Fixed(result.CurrentValue)} · 평균 {Fixed(result.Mean)} · " +
               $"표준편차 {Fixed(result.StdDev)} · z-score {Fixed(result.ZScore)} · 표본 {result.SampleCount}개 · 임계값 초과";
    }

    /// <summary>
    /// Linear extrapolation recorded alongside the verdict.
    /// </summary>
    /// <remarks>
    /// <see cref="AnomalyResult.EstimatedTimeToBreachSec"/> is -1 when the channel is flat, falling
    /// or already past the threshold — that is the absence of an estimate, not an estimate of zero,
    /// and it is rendered as such.
    /// </remarks>
    private static string DescribeForecast(AnomalyResult result)
    {
        if (!result.HasVerdict)
        {
            return $"[{result.ChannelName}] 외삽 {NoData} (기울기가 계산된 판정이 없습니다)";
        }

        string breach = result.EstimatedTimeToBreachSec > 0
            ? $"경고선 도달 예상 {result.EstimatedTimeToBreachSec:F0}초 후"
            : "상승 추세 아님 — 도달 시점 산출 안 됨";

        return $"[{result.ChannelName}] 기울기 {Fixed(result.TrendPerSecond)}/s · " +
               $"{TelemetryMlAnalyticsEngine.ForecastHorizonSec:F0}초 후 외삽값 {Fixed(result.PredictedValueIn60s)} · {breach}";
    }

    private static string Fixed(double value) =>
        double.IsNaN(value) || double.IsInfinity(value)
            ? NoData
            : value.ToString("F2", CultureInfo.InvariantCulture);

    private static string FormatSigma(double value) =>
        value.ToString("F1", CultureInfo.InvariantCulture) + " σ";

    private void SldThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Fires once during InitializeComponent, before the engine field is assigned.
        if (_mlEngine is null || TxtThresholdVal is null) return;

        _mlEngine.ZScoreThreshold = e.NewValue;
        TxtThresholdVal.Text = FormatSigma(e.NewValue);

        // Only the analyzer id in the caption depends on the threshold; the recorded verdicts were
        // reached under the old setting and are left exactly as the engine stored them.
        TxtEngineInfo.Text = DescribeEngine();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => ShowRecordedAnomalies();

    private void BtnClearAnomalies_Click(object sender, RoutedEventArgs e)
    {
        _mlEngine.ClearRecentAnomalies();
        ShowRecordedAnomalies();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
