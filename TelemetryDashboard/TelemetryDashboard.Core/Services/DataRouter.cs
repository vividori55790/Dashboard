namespace TelemetryDashboard.Core.Services;

using System.Collections.Concurrent;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Parsers;
using TelemetryDashboard.Core.Plugins;

public sealed class DataRouter : IDataRouter
{
    private readonly ConcurrentDictionary<string, SensorNode> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RoutingRule> _rules = new();
    private readonly List<IPlugin> _activePlugins = new();
    private readonly FormulaEvaluator _formulaEvaluator = new();

    public event EventHandler<TelemetryPacket>? PacketRouted;

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
        _rules[rule.Id] = rule;
        return true;
    }

    public bool UnregisterRule(string ruleId)
    {
        return _rules.TryRemove(ruleId, out _);
    }

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
        foreach (var rule in _rules.Values)
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

    private void ProcessAndEmit(TelemetryPacket pkt, List<TelemetryPacket> collector)
    {
        if (_nodes.TryGetValue(pkt.NodeId, out var node))
        {
            bool alarm = node.UpdateVariable(pkt.Variable, pkt.Value);
            if (alarm)
            {
                pkt.Flags |= PacketFlags.AlarmExceeded;
            }
        }

        collector.Add(pkt);
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
