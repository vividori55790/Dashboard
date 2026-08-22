using System.Collections.Generic;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Core.Interfaces;

/// <summary>
/// The part of a generated source an operator is allowed to move.
/// </summary>
/// <remarks>
/// This exists so the headless product stops being read-only. A browser could already watch, query
/// and be alerted; it could not change anything, because the one class that accepts a setpoint —
/// <see cref="ProfileSimulatorEngine"/> — was reachable only from the WPF shell. The streaming
/// server even raised a <c>CommandReceived</c> event for text arriving on the WebSocket, and
/// nothing anywhere subscribed to it.
/// <para>
/// What that costs is commissioning. An engineer installing this has to prove the alarm fires and
/// the interlock trips before trusting either, and without a way to put a channel at a chosen value
/// the only proof available is over-volting real hardware.
/// </para>
/// <para>
/// Deliberately implemented by generated sources only. On a rig reading a real device there is
/// nothing here to offer: moving that machine is a command to the machine, which is what the
/// emergency interlock is and which is armed separately and on purpose.
/// </para>
/// </remarks>
public interface ISimulatedControl
{
    /// <summary>The profile describing what may be commanded.</summary>
    MonitoringProfile Profile { get; }

    /// <summary>
    /// Moves one channel's setpoint, clamped to the range the profile declares.
    /// </summary>
    /// <returns>The value actually applied, or NaN when the channel is not declared.</returns>
    double SetSetpoint(string channelId, double value);

    /// <summary>The setpoint in force, or NaN when the channel is not declared.</summary>
    double GetSetpoint(string channelId);

    /// <summary>Applies a named scenario.</summary>
    /// <returns>Channel ids the scenario names that the profile does not declare.</returns>
    IReadOnlyList<string> ApplyScenario(string scenarioId);

    /// <summary>Returns every channel to the value the profile calls nominal.</summary>
    void Reset();

    /// <summary>Samples per second this source produces for each channel.</summary>
    /// <remarks>
    /// Published because a caller cannot decide whether a requested waveform is representable
    /// without it: above half this rate the samples are indistinguishable from a slower wave, and
    /// the spectrum would report a peak that is real, wrong and symptomless.
    /// </remarks>
    double SampleRateHz { get; }

    /// <summary>Drives a channel with a known waveform instead of the drift model.</summary>
    /// <returns>False when the profile declares no such channel.</returns>
    bool InjectSignal(Analytics.InjectedSignal signal);

    /// <summary>Returns a channel to the drift model.</summary>
    /// <returns>False when no signal was driving it.</returns>
    bool ClearSignal(string channelId);

    /// <summary>Signals currently driving a channel, keyed by channel id.</summary>
    /// <remarks>
    /// Reported so a screen can say which channels are a reference rather than a simulation of the
    /// machine. A driven channel that looks like every other one invites a reading of the chart as
    /// though the converter were doing that.
    /// </remarks>
    IReadOnlyDictionary<string, Analytics.InjectedSignal> InjectedSignals { get; }
}
