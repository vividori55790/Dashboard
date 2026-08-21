using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// Profile frames sent through an in-memory port, so a run has a serial path with no device on it.
/// </summary>
/// <remarks>
/// The point is the return direction. <c>--simulate</c> gives telemetry but no port, so the
/// emergency interlock — refused without <c>--serial</c> — cannot be exercised at all on a machine
/// with no MCU attached. This gives both: frames arrive the way a device's would, and a command the
/// host writes lands somewhere it can be seen.
/// <para>
/// Marked simulated, and the mark is not negotiable. The frames are generated, and a loopback run
/// that reported <c>REAL_HARDWARE</c> would put synthetic readings into the archive under a name
/// that says a machine produced them — the exact confusion the <c>SIM:</c> prefix exists to stop.
/// </para>
/// </remarks>
public sealed class LoopbackTelemetrySource : ITelemetrySource
{
    /// <summary>The value of <c>--serial</c> that selects this source.</summary>
    public const string PortToken = "loopback";

    /// <summary>Port name the in-memory device answers to.</summary>
    public const string DefaultPortName = "LOOPBACK";

    private readonly ProfileSimulatorEngine _engine;
    private readonly LoopbackSerialManager _manager = new();

    public LoopbackTelemetrySource(MonitoringProfile? profile = null)
    {
        Profile = profile ?? MonitoringProfileSet.Default;
        _engine = new ProfileSimulatorEngine(Profile);
    }

    /// <summary>The profile whose channels this source is producing.</summary>
    public MonitoringProfile Profile { get; }

    /// <inheritdoc />
    public string Origin => "SIMULATED";

    /// <inheritdoc />
    public bool IsSimulated => true;

    /// <inheritdoc />
    public string Description =>
        $"loopback port {DefaultPortName} -- {Profile.DisplayName}, {Profile.Channels.Count} synthetic channel(s)";

    /// <summary>The manager holding the in-memory port, for the interlock and for plugins.</summary>
    public ISerialManager SerialManager => _manager;

    /// <summary>Commands the host has written to the port.</summary>
    public IReadOnlyCollection<string> Written => _manager.Written;

    /// <summary>Opens the in-memory port. Cannot fail, which is itself worth saying.</summary>
    public async Task<bool> OpenAsync(CancellationToken cancellationToken)
    {
        await _manager.ConnectPortAsync(DefaultPortName, 115200, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RawPacket> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _engine.StartSimulation();

        // Every frame goes in one side of the port and is read back out the other. Yielding the
        // generator's own packet instead would skip the buffer entirely, and the loopback would be
        // proving that the simulator works rather than that the port path does.
        Task pushing = Task.Run(async () =>
        {
            await foreach (RawPacket generated in _engine.StreamSimulatedPackets(cancellationToken).ConfigureAwait(false))
            {
                _manager.Deliver(DefaultPortName, generated.RawLine);
            }
        }, cancellationToken);

        try
        {
            await foreach (RawPacket packet in
                _manager.PacketReader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return packet;
            }
        }
        finally
        {
            _engine.StopSimulation();
            _manager.Complete();

            // Observed rather than abandoned: a generator that faulted would otherwise take its
            // reason with it and leave a port that simply went quiet.
            try { await pushing.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _engine.StopSimulation();
        await _manager.DisposeAsync().ConfigureAwait(false);
    }
}
