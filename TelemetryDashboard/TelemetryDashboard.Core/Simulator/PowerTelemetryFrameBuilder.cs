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
    public required object WireFrame { get; init; }

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
/// The wire field names are held stable because the bundled web consoles bind to them. What
/// changed is their provenance: every <c>anomalyScore</c> now comes from
/// <see cref="TelemetryMlAnalyticsEngine"/> scoring the same numbers the operator sees, so the
/// simulator exercises the real detection path instead of narrating a scripted one.
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

        object frame = new
        {
            timestamp = DateTime.Now.ToString("o"),
            type = "POWER_GRID_TELEMETRY",
            scenario = state.Scenario.ToString(),
            grid = new
            {
                voltage = state.GridVoltage,
                frequency = state.GridFrequency,
                powerKw = state.GridPowerKw,
                status = state.GridStatus
            },
            dab = new
            {
                nodeId = "DAB_CONVERTER",
                mode = state.DabMode,
                dcBusVoltage = state.DabBusVoltage,
                batteryVoltage = state.DabBatteryVoltage,
                batteryCurrent = state.DabBatteryCurrent,
                batterySoC = state.DabStateOfCharge,
                powerKw = state.DabPowerKw,
                efficiency = state.DabEfficiency,
                phaseShift = state.DabPhaseShift,
                temp = state.DabTemperature,
                anomalyScore = dab.ZScore,
                isAnomaly = dab.IsAnomaly
            },
            psfb = new
            {
                nodeId = "PSFB_CONVERTER",
                mode = state.PsfbMode,
                inputDcVoltage = state.PsfbInputVoltage,
                serverVoltage = state.PsfbOutputVoltage,
                serverCurrent = state.PsfbOutputCurrent,
                powerKw = state.PsfbPowerKw,
                efficiency = state.PsfbEfficiency,
                phaseShift = state.PsfbPhaseShift,
                temp = state.PsfbTemperature,
                serverLoad = state.ServerLoadPercent,
                anomalyScore = psfb.ZScore,
                isAnomaly = psfb.IsAnomaly
            },
            alarm = new
            {
                hasAlarm = alarm,
                severity = peak >= 3.5 ? "CRITICAL" : peak >= 2.0 ? "WARNING" : "NORMAL",
                title = DescribeTitle(dab, psfb),
                message = DescribeMessage(state, dab, psfb),
                zScore = peak
            }
        };

        return new ScoredPowerFrame
        {
            State = state,
            Dab = dab,
            Psfb = psfb,
            Ambient = ambient,
            Vibration = vibration,
            WireFrame = frame
        };
    }

    /// <summary>Builds the plain sensor frame for the ambient channels.</summary>
    public object BuildAmbientFrame(PowerPlantState state, AnomalyResult ambient) => new
    {
        timestamp = DateTime.Now.ToString("o"),
        nodeId = "COM3",
        temp = state.AmbientTemperature,
        humidity = state.AmbientHumidity,
        vibration = state.Vibration,
        rpm = state.Rpm,
        anomalyScore = ambient.ZScore,
        isAnomaly = ambient.IsAnomaly,
        predictedTemp60s = ambient.PredictedValueIn60s
    };

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
