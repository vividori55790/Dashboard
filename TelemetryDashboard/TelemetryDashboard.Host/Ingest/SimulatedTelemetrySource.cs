using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// Synthetic telemetry from the dual-MCU virtual simulator. Opt-in only.
/// </summary>
/// <remarks>
/// Nothing in the host reaches for this source on its own. It exists so the ingest path, the
/// stream and the console can be exercised without hardware, and it is reachable only through an
/// explicit <c>--simulate</c>. Every frame it produces is marked twice — <c>simulated: true</c> on
/// the wire and a <c>SIM:</c> node prefix that follows the sample into the DVR timeline and the CSV
/// recording — because the mark is worthless if it disappears the moment the data is stored.
/// </remarks>
public sealed class SimulatedTelemetrySource : ITelemetrySource
{
    private readonly DualMcuVirtualSimulatorEngine _engine = new();

    /// <inheritdoc />
    public string Origin => "SIMULATED";

    /// <inheritdoc />
    public bool IsSimulated => true;

    /// <inheritdoc />
    public string Description => "DualMcuVirtualSimulatorEngine -- synthetic waveforms, not measurements";

    /// <inheritdoc />
    public IAsyncEnumerable<RawPacket> ReadAsync(CancellationToken cancellationToken)
    {
        _engine.StartSimulation();
        return _engine.StreamSimulatedPackets(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _engine.StopSimulation();
        return _engine.DisposeAsync();
    }
}
