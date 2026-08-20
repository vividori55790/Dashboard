using System.Collections.Generic;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Services;

/// <summary>
/// The routing rules registered when nobody has configured any.
/// </summary>
/// <remarks>
/// Only the framing this repository's own firmware and simulator emit — <c>$TELE,node,var,value,unit</c>
/// with an XOR checksum, and the <c>$HIST</c> resync frame the prefix parser handles alongside it.
/// A rule that does not match costs nothing: the parser rejects the line and the raw fallback sees
/// it instead. That is why this list can be a default at all — it recognises a documented format
/// rather than assuming one, and a device speaking anything else is never mislabelled by it.
/// <para>
/// In Core rather than in the host because both front ends need the same answer. The desktop shell
/// registered nothing at all, so every frame it received missed the router entirely and landed in
/// the raw fallback, which named the first number in the line "Temperature" whatever it was.
/// </para>
/// </remarks>
public static class DefaultRoutingRules
{
    /// <summary>Prefix tag of the standard telemetry frame.</summary>
    public const string TelemetryTag = "TELE";

    /// <summary>Builds the default rule set. Applies to every port.</summary>
    public static IReadOnlyList<RoutingRule> Create() => new[]
    {
        new RoutingRule
        {
            Id = "default-tele",
            RuleType = RuleType.Prefix,
            Tag = TelemetryTag,
            Port = "*",
            TargetNodeId = string.Empty
        }
    };
}
