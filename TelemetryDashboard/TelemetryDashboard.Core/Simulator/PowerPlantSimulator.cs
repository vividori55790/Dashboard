using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Simulator;

/// <summary>
/// Channel ids the built-in simulator reads its setpoints under.
/// </summary>
/// <remarks>
/// These are the names a <see cref="MonitoringProfile"/> uses to say which quantity a slider drives.
/// Naming them once, here, is what lets a profile be data: the simulator looks up the ids it knows
/// and ignores the rest, so a site can describe channels this model has never heard of without
/// either side gaining a reference to the other.
/// </remarks>
public static class SimulatorChannelIds
{
    public const string GridVoltage = "grid.voltage";
    public const string DabBusVoltage = "dab.bus_voltage";
    public const string PsfbOutputVoltage = "psfb.output_voltage";
    public const string ServerLoad = "server.load";

    public const string AmbientTemperature = "ambient.temperature";
    public const string AmbientHumidity = "ambient.humidity";
    public const string MachineVibration = "machine.vibration";
    public const string MachineSpeed = "machine.speed";
}

/// <summary>Operating scenario driven from the control panel or the web console.</summary>
public enum PowerScenario
{
    Normal,
    GridOutage,
    DabOvercurrent,
    PsfbUnderVoltage
}

/// <summary>One simulated instant of the grid / DAB / PSFB power chain.</summary>
public sealed class PowerPlantState
{
    public double ElapsedSec { get; init; }
    public PowerScenario Scenario { get; init; }

    public double GridVoltage { get; init; }
    public double GridFrequency { get; init; }
    public double GridPowerKw { get; init; }
    public string GridStatus { get; init; } = "NORMAL";

    public string DabMode { get; init; } = "STANDBY";
    public double DabBusVoltage { get; init; }
    public double DabBatteryVoltage { get; init; }
    public double DabBatteryCurrent { get; init; }
    public double DabStateOfCharge { get; init; }
    public double DabPowerKw { get; init; }
    public double DabEfficiency { get; init; }
    public double DabPhaseShift { get; init; }
    public double DabTemperature { get; init; }

    public string PsfbMode { get; init; } = "48V REGULATED";
    public double PsfbInputVoltage { get; init; }
    public double PsfbOutputVoltage { get; init; }
    public double PsfbOutputCurrent { get; init; }
    public double PsfbPowerKw { get; init; }
    public double PsfbEfficiency { get; init; }
    public double PsfbPhaseShift { get; init; }
    public double PsfbTemperature { get; init; }
    public double ServerLoadPercent { get; init; }

    public double AmbientTemperature { get; init; }
    public double AmbientHumidity { get; init; }
    public double Vibration { get; init; }
    public double Rpm { get; init; }
}

/// <summary>
/// Reads the measured value of a profile channel out of a simulated instant.
/// </summary>
/// <remarks>
/// The setpoint direction of the profile contract was already there — a slider writes a value under
/// a channel id and the model reads it. This is the return path, and it exists so a chart can plot
/// whichever channels the selected profile declares instead of a fixed four.
/// <para>
/// The failure mode is the point. An id this model does not measure returns false and is plotted by
/// nobody. A profile may perfectly legitimately name a quantity the built-in demo model has never
/// heard of, and the honest response to that is an absent trace, never a substituted or scaled one.
/// </para>
/// </remarks>
public static class SimulatorChannelReadings
{
    /// <summary>The value this model computed for a channel, if it computes that channel at all.</summary>
    public static bool TryRead(PowerPlantState state, string channelId, out double value)
    {
        ArgumentNullException.ThrowIfNull(state);

        switch (channelId)
        {
            case SimulatorChannelIds.GridVoltage: value = state.GridVoltage; return true;
            case SimulatorChannelIds.DabBusVoltage: value = state.DabBusVoltage; return true;
            case SimulatorChannelIds.PsfbOutputVoltage: value = state.PsfbOutputVoltage; return true;
            case SimulatorChannelIds.ServerLoad: value = state.ServerLoadPercent; return true;
            case SimulatorChannelIds.AmbientTemperature: value = state.AmbientTemperature; return true;
            case SimulatorChannelIds.AmbientHumidity: value = state.AmbientHumidity; return true;
            case SimulatorChannelIds.MachineVibration: value = state.Vibration; return true;
            case SimulatorChannelIds.MachineSpeed: value = state.Rpm; return true;
            default: value = 0.0; return false;
        }
    }
}

/// <summary>
/// Deterministic physical model of the UPS power chain used by the built-in demo.
/// </summary>
/// <remarks>
/// This produces <em>measurements only</em>. It deliberately exposes no anomaly score: scores are
/// computed downstream by the analytics engine from these values, exactly as they are for real
/// hardware. The window previously substituted literals — <c>scenario == "DAB_ANOMALY" ? 3.84</c> —
/// so the displayed sigma was a stage prop that no longer tracked the data it claimed to describe.
/// <para>Seeded so a given scenario replays identically, which makes incidents reproducible.</para>
/// </remarks>
public sealed class PowerPlantSimulator
{
    private readonly Random _random;

    public PowerPlantSimulator(int seed = 20260819)
    {
        _random = new Random(seed);
    }

    public PowerScenario Scenario { get; set; } = PowerScenario.Normal;

    /// <summary>
    /// Operator setpoints keyed by <see cref="SimulatorChannelIds"/>. Absent means "use the model's
    /// own resting value", which is why every read below carries its fallback: a profile that names
    /// only two channels leaves the rest running exactly as they did before it was selected.
    /// </summary>
    private readonly Dictionary<string, double> _setpoints = new(StringComparer.OrdinalIgnoreCase);

    public double GridVoltageSetpoint
    {
        get => Setpoint(SimulatorChannelIds.GridVoltage, 380.0);
        set => SetSetpoint(SimulatorChannelIds.GridVoltage, value);
    }

    public double DabBusVoltageSetpoint
    {
        get => Setpoint(SimulatorChannelIds.DabBusVoltage, 400.0);
        set => SetSetpoint(SimulatorChannelIds.DabBusVoltage, value);
    }

    public double PsfbVoltageSetpoint
    {
        get => Setpoint(SimulatorChannelIds.PsfbOutputVoltage, 48.05);
        set => SetSetpoint(SimulatorChannelIds.PsfbOutputVoltage, value);
    }

    public double ServerLoadSetpoint
    {
        get => Setpoint(SimulatorChannelIds.ServerLoad, 82.4);
        set => SetSetpoint(SimulatorChannelIds.ServerLoad, value);
    }

    public double ElapsedSec { get; private set; }

    /// <summary>Drives one channel. An id the model does not read is stored and simply not used.</summary>
    public void SetSetpoint(string channelId, double value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        _setpoints[channelId] = value;
    }

    /// <summary>The operator's value for a channel, or <paramref name="restingValue"/> if unset.</summary>
    public double Setpoint(string channelId, double restingValue) =>
        channelId is not null && _setpoints.TryGetValue(channelId, out double value) ? value : restingValue;

    /// <summary>Restores every setpoint and clears the active scenario.</summary>
    public void Reset()
    {
        Scenario = PowerScenario.Normal;
        _setpoints.Clear();
    }

    /// <summary>Advances the model and returns the resulting instantaneous state.</summary>
    public PowerPlantState Advance(double deltaSec)
    {
        ElapsedSec += deltaSec;
        double t = ElapsedSec;

        bool outage = Scenario == PowerScenario.GridOutage;

        double dabBus = DabBusVoltageSetpoint + Math.Sin(t * 0.8) * 0.4;
        double dabBatteryCurrent = outage
            ? -32.5 + Noise(0.5)
            : 12.4 + Noise(0.2);
        double dabTemperature = 40.0 + Math.Sin(t * 0.3);

        double psfbOut = PsfbVoltageSetpoint + Math.Sin(t * 1.2) * 0.03;
        double psfbCurrent = ServerLoadSetpoint / 100.0 * 300.0 + Noise(1.0);
        double psfbTemperature = 44.0 + Math.Cos(t * 0.2);

        // Fault injection perturbs the physical quantities; the anomaly score that results is
        // whatever the analytics engine computes from them.
        switch (Scenario)
        {
            case PowerScenario.DabOvercurrent:
                dabBatteryCurrent = 68.4 + _random.NextDouble() * 3.0;
                dabBus = 425.0 + Math.Sin(t * 3.0) * 5.0;
                dabTemperature = 88.5 + Math.Sin(t * 2.0) * 2.0;
                break;

            case PowerScenario.PsfbUnderVoltage:
                psfbOut = 41.2 - _random.NextDouble();
                psfbCurrent = 295.0 + _random.NextDouble() * 5.0;
                psfbTemperature = 92.0 + Math.Sin(t * 2.0) * 2.0;
                break;
        }

        double psfbPowerKw = psfbOut * psfbCurrent / 1000.0;
        double dabPowerKw = psfbPowerKw / 0.98;

        return new PowerPlantState
        {
            ElapsedSec = t,
            Scenario = Scenario,

            GridVoltage = outage ? 0.0 : GridVoltageSetpoint + Math.Sin(t) * 0.5,
            GridFrequency = outage ? 0.0 : 60.0 + Noise(0.02),
            GridPowerKw = outage ? 0.0 : dabPowerKw,
            GridStatus = outage ? "OUTAGE" : "NORMAL",

            DabMode = outage ? "DISCHARGING" : "STANDBY",
            DabBusVoltage = dabBus,
            DabBatteryVoltage = 384.5 + Math.Sin(t * 0.1) * 0.1,
            DabBatteryCurrent = dabBatteryCurrent,
            DabStateOfCharge = 94.5 - t * 0.0005,
            DabPowerKw = dabPowerKw,
            DabEfficiency = 97.8,
            DabPhaseShift = 18.4,
            DabTemperature = dabTemperature,

            PsfbInputVoltage = dabBus,
            PsfbOutputVoltage = psfbOut,
            PsfbOutputCurrent = psfbCurrent,
            PsfbPowerKw = psfbPowerKw,
            PsfbEfficiency = 98.4,
            PsfbPhaseShift = 38.2,
            PsfbTemperature = psfbTemperature,
            ServerLoadPercent = ServerLoadSetpoint,

            // The generic profile's four channels. Each resting value is the constant this model
            // has always used, so an unselected channel behaves exactly as it did before profiles
            // existed, and a slider that is moved genuinely moves the measurement.
            AmbientTemperature = Setpoint(SimulatorChannelIds.AmbientTemperature, 25.0)
                + 5.0 * Math.Sin(t * 0.8) + Noise(1.0),
            AmbientHumidity = Setpoint(SimulatorChannelIds.AmbientHumidity, 50.0)
                + 10.0 * Math.Cos(t * 0.5) + Noise(1.0),
            Vibration = Math.Abs(Setpoint(SimulatorChannelIds.MachineVibration, 0.2) * Math.Sin(t * 3.0)
                + 0.05 * _random.NextDouble()),
            Rpm = Setpoint(SimulatorChannelIds.MachineSpeed, 1200.0)
                + 150.0 * Math.Sin(t * 1.5) + Noise(10.0)
        };
    }

    private double Noise(double amplitude) => (_random.NextDouble() - 0.5) * amplitude;
}
