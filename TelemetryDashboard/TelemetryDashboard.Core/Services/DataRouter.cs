namespace TelemetryDashboard.Core.Services;

using System.Collections.Concurrent;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Parsers;
using TelemetryDashboard.Core.Plugins;

public sealed class DataRouter : IDataRouter
{
    private readonly ConcurrentDictionary<string, SensorNode> _nodes = new(StringComparer.OrdinalIgnoreCase);
    // Copy on write, behind a lock for the writers and a single volatile read for the router.
    // Replacing a rule set has to be one step: the desktop loads a file while frames are already
    // arriving, and a clear-then-add would drop every line in the gap onto the positional parser,
    // which names them Field_1 and charts them.
    private readonly object _ruleGate = new();
    private volatile IReadOnlyDictionary<string, RoutingRule> _rules =
        new Dictionary<string, RoutingRule>(StringComparer.Ordinal);
    private readonly List<IPlugin> _activePlugins = new();
    private readonly FormulaEvaluator _formulaEvaluator = new();

    public event EventHandler<TelemetryPacket>? PacketRouted;

    /// <summary>
    /// Whether the packets this router produces came from a synthetic source.
    /// </summary>
    /// <remarks>
    /// Set once per run from the attached source. It exists because the mark used to be applied
    /// further downstream, on the publish path, and plugins are delivered to from here — so a
    /// plugin received two hundred simulated packets carrying no <see cref="PacketFlags.Simulated"/>
    /// bit and an unprefixed node id, while the start-up banner promised that every frame carried
    /// both. A plugin that cannot tell synthetic data from a measurement can write it into a report
    /// as a measurement, which is the failure this project exists to prevent.
    /// </remarks>
    public bool SourceIsSimulated { get; set; }

    public void RegisterNode(SensorNode node)
    {
        _nodes[node.NodeId] = node;
    }

    public SensorNode GetNode(string nodeId)
    {
        return _nodes.TryGetValue(nodeId, out var node) ? node : new SensorNode { NodeId = nodeId };
    }

    public bool RegisterRule(RoutingRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        lock (_ruleGate)
        {
            var next = new Dictionary<string, RoutingRule>(_rules, StringComparer.Ordinal)
            {
                [rule.Id] = rule
            };
            _rules = next;
        }

        return true;
    }

    public bool UnregisterRule(string ruleId)
    {
        lock (_ruleGate)
        {
            if (!_rules.ContainsKey(ruleId)) return false;

            var next = new Dictionary<string, RoutingRule>(_rules, StringComparer.Ordinal);
            next.Remove(ruleId);
            _rules = next;
            return true;
        }
    }

    /// <summary>
    /// Swaps the whole rule set in one step.
    /// </summary>
    /// <remarks>
    /// A file describing the device on the bench replaces the built-ins rather than joining them,
    /// which is the same decision the headless host makes: two rules claiming one frame is not two
    /// configurations, it is one ambiguity, and which of them won would differ between runs.
    /// </remarks>
    public void ReplaceRules(IEnumerable<RoutingRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var next = new Dictionary<string, RoutingRule>(StringComparer.Ordinal);
        foreach (RoutingRule rule in rules) next[rule.Id] = rule;

        lock (_ruleGate) _rules = next;
    }

    /// <summary>The rules currently in force, for anything that has to report them.</summary>
    public IReadOnlyCollection<RoutingRule> Rules => (IReadOnlyCollection<RoutingRule>)_rules.Values;

    public void RegisterPlugin(IPlugin plugin)
    {
        lock (_activePlugins)
        {
            _activePlugins.Add(plugin);
        }
    }

    public IEnumerable<TelemetryPacket> Route(RawPacket rawPacket)
    {
        var outputPackets = new List<TelemetryPacket>();

        // 1. Dual-Track Plugin First Pass
        lock (_activePlugins)
        {
            foreach (var plugin in _activePlugins)
            {
                if (plugin.TryCustomParse(rawPacket, out var pluginPackets))
                {
                    foreach (var pkt in pluginPackets)
                    {
                        ProcessAndEmit(pkt, outputPackets);
                    }
                    return outputPackets;
                }
            }
        }

        // 2. Built-in High-Speed Engine Pass
        foreach (RoutingRule rule in _rules.Values)
        {
            if (rule.Port != "*" && !string.Equals(rule.Port, rawPacket.PortName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            List<TelemetryPacket>? parsed = null;
            bool success = rule.RuleType switch
            {
                RuleType.Prefix => PrefixParser.TryParse(rawPacket, rule, out parsed),
                RuleType.Json => JsonParser.TryParse(rawPacket, rule, out parsed),
                RuleType.Columns => ColumnsParser.TryParse(rawPacket, rule, out parsed),
                _ => false
            };

            if (success && parsed != null)
            {
                foreach (var pkt in parsed)
                {
                    ProcessAndEmit(pkt, outputPackets);
                }

                // 3. Dynamic Linked Formulas Evaluation
                EvaluateFormulas(rule, rawPacket.Timestamp, outputPackets);
                break;
            }
        }

        return outputPackets;
    }

    /// <summary>
    /// Puts a packet that was parsed elsewhere through the same delivery path as a routed one.
    /// </summary>
    /// <remarks>
    /// Plugins subscribe here, so until this existed they saw only packets that matched a
    /// configured routing rule. Anything decoded by the JSON channel map or by positional parsing
    /// reached the chart, the archive and the wire, and reached no plugin at all — which meant a
    /// plugin watching a live network feed received nothing while every surface reported it loaded
    /// and running. The gap was invisible precisely because the data still arrived everywhere else.
    ///
    /// Alarm evaluation and the synthetic mark are applied here too, so a packet's provenance and
    /// its alarm state do not depend on which parser happened to decode it.
    /// </remarks>
    public void Deliver(TelemetryPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ProcessAndEmit(packet, null);
    }

    private void ProcessAndEmit(TelemetryPacket pkt, List<TelemetryPacket>? collector)
    {
        if (_nodes.TryGetValue(pkt.NodeId, out var node))
        {
            bool alarm = node.UpdateVariable(pkt.Variable, pkt.Value);
            if (alarm)
            {
                pkt.Flags |= PacketFlags.AlarmExceeded;
            }
        }

        // Marked here, after the node lookup above so configured node names still match, and before
        // anything downstream sees the packet -- plugins included.
        if (SourceIsSimulated)
        {
            pkt.Flags |= PacketFlags.Simulated;
            pkt.NodeId = SimulatedNodeMarker.Apply(pkt.NodeId);
        }

        collector?.Add(pkt);
        PacketRouted?.Invoke(this, pkt);

        lock (_activePlugins)
        {
            foreach (var plugin in _activePlugins)
            {
                plugin.OnPacketReceived(pkt);
            }
        }
    }

    private void EvaluateFormulas(RoutingRule rule, DateTime timestamp, List<TelemetryPacket> collector)
    {
        foreach (string formulaExpr in rule.Formulas)
        {
            string varName = "derived";
            string expr = formulaExpr;

            if (formulaExpr.Contains('='))
            {
                var parts = formulaExpr.Split('=', 2);
                varName = parts[0].Trim();
                expr = parts[1].Trim();
            }

            double result = _formulaEvaluator.Evaluate(expr, rule.TargetNodeId, (nId, vName) =>
            {
                if (_nodes.TryGetValue(nId, out var n) && n.LatestValues.TryGetValue(vName, out double val))
                {
                    return val;
                }
                return 0.0;
            });

            var derivedPkt = new TelemetryPacket
            {
                NodeId = rule.TargetNodeId,
                Variable = varName,
                Value = result,
                Timestamp = timestamp,
                Flags = PacketFlags.IsDerived
            };

            ProcessAndEmit(derivedPkt, collector);
        }
    }
}
