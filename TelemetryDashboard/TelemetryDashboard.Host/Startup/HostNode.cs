using System;
using TelemetryDashboard.Core.Cluster;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// This installation's identity, resolved once for the life of the process.
/// </summary>
/// <remarks>
/// A single resolution point on purpose. Two parts of the host each calling
/// <see cref="NodeIdentity.LoadOrCreate"/> would each generate an identity on a first run where the
/// file could not be written, and the process would then be publishing under two different names at
/// once — a fault that shows up only as one machine's data appearing to come from two.
///
/// The environment variable exists for managed fleets that assign their own names. An assigned name
/// that is malformed stops the start rather than being sanitised, because sanitising can quietly
/// map two different inputs onto the same identity.
/// </remarks>
public static class HostNode
{
    /// <summary>Environment variable naming this node explicitly, for a fleet that manages its own.</summary>
    public const string AssignedIdVariable = "TELEMETRY_HOST_NODE_ID";

    private static readonly Lazy<NodeIdentity> Resolved = new(Resolve, isThreadSafe: true);

    /// <summary>Who this installation is.</summary>
    public static NodeIdentity Identity => Resolved.Value;

    private static NodeIdentity Resolve()
    {
        string? assigned = Environment.GetEnvironmentVariable(AssignedIdVariable);

        return string.IsNullOrWhiteSpace(assigned)
            ? NodeIdentity.LoadOrCreate(AppContext.BaseDirectory)
            : NodeIdentity.FromAssignedId(assigned);
    }
}
