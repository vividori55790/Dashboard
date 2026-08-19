using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Core.Protocols;

/// <summary>
/// Open registry of protocol bridge adapters keyed by protocol name.
/// </summary>
/// <remarks>
/// Supporting a new fieldbus means writing one <see cref="IProtocolBridge"/> file and registering
/// it — no existing file is edited and no switch statement grows. Adapters can also be registered
/// at runtime by the plugin sandbox, so the gateway keeps pace with new protocols without a rebuild.
/// </remarks>
public sealed class ProtocolBridgeRegistry
{
    private readonly ConcurrentDictionary<string, IProtocolBridge> _bridges =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a registry preloaded with the adapters shipped in the box.</summary>
    public static ProtocolBridgeRegistry CreateDefault()
    {
        var registry = new ProtocolBridgeRegistry();
        registry.Register(new CanBusBridgeAdapter());
        registry.Register(new ModbusBridgeAdapter());
        registry.Register(new Ros2BridgeAdapter());
        return registry;
    }

    /// <summary>Protocol names currently available, in registration-independent alphabetical order.</summary>
    public IReadOnlyList<string> ProtocolNames => _bridges.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

    public int Count => _bridges.Count;

    /// <summary>Registers or replaces the adapter serving <see cref="IProtocolBridge.ProtocolName"/>.</summary>
    public void Register(IProtocolBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);

        if (string.IsNullOrWhiteSpace(bridge.ProtocolName))
        {
            throw new ArgumentException("A protocol bridge must expose a non-empty ProtocolName.", nameof(bridge));
        }

        _bridges[bridge.ProtocolName] = bridge;
    }

    public bool Unregister(string protocolName) =>
        !string.IsNullOrWhiteSpace(protocolName) && _bridges.TryRemove(protocolName, out _);

    public bool TryResolve(string protocolName, out IProtocolBridge bridge)
    {
        if (string.IsNullOrWhiteSpace(protocolName))
        {
            bridge = default!;
            return false;
        }

        return _bridges.TryGetValue(protocolName, out bridge!);
    }

    /// <summary>Resolves an adapter by name, or null when the protocol is not registered.</summary>
    public IProtocolBridge? Resolve(string protocolName) =>
        TryResolve(protocolName, out IProtocolBridge bridge) ? bridge : null;
}
