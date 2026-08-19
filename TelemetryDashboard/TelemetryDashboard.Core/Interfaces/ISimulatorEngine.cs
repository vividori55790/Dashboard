namespace TelemetryDashboard.Core.Interfaces;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Models;

/// <summary>
/// Interface contract for Dual-MCU Virtual Simulator Engine.
/// </summary>
public interface ISimulatorEngine : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Indicates whether the simulation loop is active.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Starts generating synthetic telemetry packets.
    /// </summary>
    void StartSimulation();

    /// <summary>
    /// Stops generating synthetic telemetry packets.
    /// </summary>
    void StopSimulation();

    /// <summary>
    /// Streams simulated raw packets asynchronously.
    /// </summary>
    IAsyncEnumerable<RawPacket> StreamSimulatedPackets(CancellationToken cancellationToken = default);
}
