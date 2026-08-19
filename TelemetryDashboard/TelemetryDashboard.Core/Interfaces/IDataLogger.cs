namespace TelemetryDashboard.Core.Interfaces;

using TelemetryDashboard.Core.Models;

public record QueryFilter(
    string? NodeId = null,
    string? Variable = null,
    DateTime? StartTime = null,
    DateTime? EndTime = null,
    int Limit = 1000
);

public interface IDataLogger
{
    Task WriteAsync(TelemetryPacket packet, CancellationToken cancellationToken = default);
    Task WriteBatchAsync(IEnumerable<TelemetryPacket> packets, CancellationToken cancellationToken = default);
    Task<IEnumerable<TelemetryPacket>> QueryAsync(QueryFilter filter, CancellationToken cancellationToken = default);
}
