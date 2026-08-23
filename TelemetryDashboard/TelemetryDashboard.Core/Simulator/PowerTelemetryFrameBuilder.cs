using System;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Analytics;

namespace TelemetryDashboard.Core.Simulator;

/// <summary>Anomaly scores derived from a simulated instant, plus the wire frame carrying them.</summary>
public sealed class ScoredPowerFrame
{
    public required PowerPlantState State { get; init; }
    public required AnomalyResult Dab { get; init; }
    public required AnomalyResult Psfb { get; init; }
    public required AnomalyResult Ambient { get; init; }
    public required AnomalyResult Vibration { get; init; }

    public double PeakZScore => Math.Max(Dab.ZScore, Psfb.ZScore);
    public bool HasAlarm => Dab.IsAnomaly || Psfb.IsAnomaly;

    public string Severity => PeakZScore >= 3.5 ? "CRITICAL"
        : PeakZScore >= 2.0 ? "WARNING"
        : "NORMAL";
}

/// <summary>
/// Scores a simulated power-chain instant with the production analytics engine and packages it
/// for broadcast.
/// </summary>
/// <remarks>
/// Scores only. This used to build two broadcast frames as well, in the shape the product used
/// before it had a wire contract -- a flat {temp, humidity, rpm} and a nested {grid, dab, psfb,
/// alarm} -- and its own remark said the bundled consoles bound to those names. Measured on the
/// running shell with a browser attached: 214 frames received, 0 that any shipped page could read,
/// because every one of them reads {nodeId, variable, value, unit} and discards the rest.
/// <para>
/// Every <c>anomalyScore</c> comes from <see cref="TelemetryMlAnalyticsEngine"/> scoring the same
/// numbers the operator sees, so the simulator exercises the real detection path rather than
/// narrating a scripted one.
/// </para>
/// </remarks>
public sealed class PowerTelemetryFrameBuilder
{
    private readonly TelemetryMlAnalyticsEngine _analytics;

    public PowerTelemetryFrameBuilder(TelemetryMlAnalyticsEngine analytics)
    {
        _analytics = analytics ?? throw new ArgumentNullException(nameof(analytics));
    }

    public ScoredPowerFrame Build(PowerPlantState state)
    {
        AnomalyResult dab = _analytics.AnalyzeChannel("DAB.BatteryCurrent", state.DabBatteryCurrent, 50.0);
        AnomalyResult psfb = _analytics.AnalyzeChannel("PSFB.ServerVoltage", state.PsfbOutputVoltage, 52.0);
        AnomalyResult ambient = _analytics.AnalyzeChannel("COM3.Temperature", state.AmbientTemperature, 90.0);
        AnomalyResult vibration = _analytics.AnalyzeChannel("COM3.Vibration", state.Vibration, 0.45);

        double peak = Math.Max(dab.ZScore, psfb.ZScore);
        bool alarm = dab.IsAnomaly || psfb.IsAnomaly;

        return new ScoredPowerFrame
        {
            State = state,
            Dab = dab,
            Psfb = psfb,
            Ambient = ambient,
            Vibration = vibration
        };
    }

    private static string DescribeTitle(AnomalyResult dab, AnomalyResult psfb)
    {
        if (dab.IsAnomaly) return "DAB 배터리 전류 이상 감지";
        if (psfb.IsAnomaly) return "PSFB 48V 서버 전압 이상 감지";
        return string.Empty;
    }

    private static string DescribeMessage(PowerPlantState state, AnomalyResult dab, AnomalyResult psfb)
    {
        if (dab.IsAnomaly)
        {
            return $"배터리 전류 {state.DabBatteryCurrent:F1}A (기준 대비 {dab.ZScore:F2}σ 편차, 평균 {dab.Mean:F1}A)";
        }

        if (psfb.IsAnomaly)
        {
            return $"서버 전압 {state.PsfbOutputVoltage:F2}V (기준 대비 {psfb.ZScore:F2}σ 편차, 평균 {psfb.Mean:F2}V)";
        }

        return string.Empty;
    }
}
