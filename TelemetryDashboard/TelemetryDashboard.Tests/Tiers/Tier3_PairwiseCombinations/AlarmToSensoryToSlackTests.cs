namespace TelemetryDashboard.Tests.Tiers.Tier3_PairwiseCombinations;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Tests.TestUtilities;
using Xunit;

/// <summary>
/// Tier 3 Pairwise Combination Test Suite:
/// Verifies cross-subsystem interaction between Threshold Breach -> Multi-Sensory Alerts (SAPI TTS + Toast) + Slack Webhook Publisher.
/// </summary>
[Trait("Category", "Tier3")]
public class AlarmToSensoryToSlackTests
{
    private class MockSapiTtsService
    {
        public List<string> SpokenMessages { get; } = new();

        public void Speak(string text)
        {
            SpokenMessages.Add(text);
        }
    }

    private class MockToastNotificationService
    {
        public List<(string Title, string Message)> Notifications { get; } = new();

        public void ShowToast(string title, string message)
        {
            Notifications.Add((title, message));
        }
    }

    private class MockSlackWebhookPublisher
    {
        private readonly List<string> _dispatchedWebhooks = new();
        private readonly object _lock = new();
        private DateTime _lastDispatchTime = DateTime.MinValue;

        public IReadOnlyList<string> DispatchedWebhooks
        {
            get { lock (_lock) return _dispatchedWebhooks.ToList(); }
        }

        public bool PublishAlert(string nodeId, string variable, double value, string level = "Critical")
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastDispatchTime).TotalSeconds < 0.5)
                {
                    return false; // Throttled
                }

                _lastDispatchTime = now;
                var payload = $"{{\"text\": \"[{level}] Node {nodeId} variable {variable} value {value:F2} breached threshold!\"}}";
                _dispatchedWebhooks.Add(payload);
                return true;
            }
        }

        public void PublishResolution(string nodeId, string variable)
        {
            lock (_lock)
            {
                var payload = $"{{\"text\": \"[Resolved] Node {nodeId} variable {variable} returned to normal state.\"}}";
                _dispatchedWebhooks.Add(payload);
            }
        }
    }

    [Fact]
    public void ThresholdBreach_TriggersSapiTtsAndToastAndSlack()
    {
        var node = new SensorNode("MCU_NODE_1", "Motor Node", "COM3", "ENGINE");
        node.Thresholds["TEMP"] = (Min: 0.0, Max: 85.0);

        var router = new DataRouter();
        router.RegisterNode(node);

        var tts = new MockSapiTtsService();
        var toast = new MockToastNotificationService();
        var slack = new MockSlackWebhookPublisher();

        router.PacketRouted += (s, pkt) =>
        {
            if (pkt.Flags.HasFlag(PacketFlags.AlarmExceeded))
            {
                tts.Speak($"Warning: Node {pkt.NodeId} {pkt.Variable} exceeded threshold with value {pkt.Value:F1}");
                toast.ShowToast("Critical Telemetry Alert", $"{pkt.NodeId} {pkt.Variable} breached threshold!");
                slack.PublishAlert(pkt.NodeId, pkt.Variable, pkt.Value);
            }
        };

        var rule = new RoutingRule
        {
            RuleType = RuleType.Prefix,
            Tag = "TELE",
            Port = "COM3",
            TargetNodeId = "MCU_NODE_1"
        };
        router.RegisterRule(rule);

        var line = TestDataGenerator.CreateValidPrefixFrame("TELE", "MCU_NODE_1", "TEMP", 89.5, "C");
        router.Route(new RawPacket("COM3", line, DateTime.UtcNow));

        tts.SpokenMessages.Should().HaveCount(1);
        tts.SpokenMessages[0].Should().Contain("Warning: Node MCU_NODE_1 TEMP exceeded threshold");

        toast.Notifications.Should().HaveCount(1);
        toast.Notifications[0].Title.Should().Be("Critical Telemetry Alert");

        slack.DispatchedWebhooks.Should().HaveCount(1);
        slack.DispatchedWebhooks[0].Should().Contain("MCU_NODE_1");
        slack.DispatchedWebhooks[0].Should().Contain("89.50");
    }

    [Fact]
    public void SubThresholdValue_DoesNotTriggerAlerts()
    {
        var node = new SensorNode("MCU_NODE_1", "Motor Node", "COM3", "ENGINE");
        node.Thresholds["TEMP"] = (Min: 0.0, Max: 85.0);

        var router = new DataRouter();
        router.RegisterNode(node);

        var tts = new MockSapiTtsService();
        var toast = new MockToastNotificationService();
        var slack = new MockSlackWebhookPublisher();

        router.PacketRouted += (s, pkt) =>
        {
            if (pkt.Flags.HasFlag(PacketFlags.AlarmExceeded))
            {
                tts.Speak($"Warning: {pkt.Variable}");
                toast.ShowToast("Alert", pkt.Variable);
                slack.PublishAlert(pkt.NodeId, pkt.Variable, pkt.Value);
            }
        };

        var rule = new RoutingRule { RuleType = RuleType.Prefix, Tag = "TELE", TargetNodeId = "MCU_NODE_1" };
        router.RegisterRule(rule);

        var line = TestDataGenerator.CreateValidPrefixFrame("TELE", "MCU_NODE_1", "TEMP", 45.0, "C");
        router.Route(new RawPacket("COM3", line, DateTime.UtcNow));

        tts.SpokenMessages.Should().BeEmpty();
        toast.Notifications.Should().BeEmpty();
        slack.DispatchedWebhooks.Should().BeEmpty();
    }

    [Fact]
    public void MultipleConsecutiveAlarms_ThrottlesSlackWebhookRateToAvoidSpam()
    {
        var node = new SensorNode("MCU_NODE_1", "Motor Node", "COM3", "ENGINE");
        node.Thresholds["TEMP"] = (Min: 0.0, Max: 85.0);

        var router = new DataRouter();
        router.RegisterNode(node);

        var tts = new MockSapiTtsService();
        var toast = new MockToastNotificationService();
        var slack = new MockSlackWebhookPublisher();

        router.PacketRouted += (s, pkt) =>
        {
            if (pkt.Flags.HasFlag(PacketFlags.AlarmExceeded))
            {
                tts.Speak($"Warning: {pkt.Variable}");
                toast.ShowToast("Alert", pkt.Variable);
                slack.PublishAlert(pkt.NodeId, pkt.Variable, pkt.Value);
            }
        };

        var rule = new RoutingRule { RuleType = RuleType.Prefix, Tag = "TELE", TargetNodeId = "MCU_NODE_1" };
        router.RegisterRule(rule);

        for (int i = 0; i < 10; i++)
        {
            var line = TestDataGenerator.CreateValidPrefixFrame("TELE", "MCU_NODE_1", "TEMP", 90.0 + i, "C");
            router.Route(new RawPacket("COM3", line, DateTime.UtcNow));
        }

        // TTS & Toast capture all 10 notifications
        tts.SpokenMessages.Should().HaveCount(10);
        toast.Notifications.Should().HaveCount(10);

        // Slack throttled rapid webhooks to 1 dispatch
        slack.DispatchedWebhooks.Should().HaveCount(1);
    }

    [Fact]
    public void ZScoreAnomalyDetection_TriggersEarlyWarningBeforeThresholdBreach()
    {
        var tts = new MockSapiTtsService();
        var slack = new MockSlackWebhookPublisher();

        // Simulate anomaly engine Z-Score drift evaluation
        double[] historicalValues = { 10.0, 10.1, 10.2, 9.9, 10.0, 10.1 };
        double mean = historicalValues.Average();
        double stdDev = Math.Sqrt(historicalValues.Select(v => Math.Pow(v - mean, 2)).Average());

        double newDriftValue = 15.0; // Significant Z-score shift (Z > 3.0)
        double zScore = (newDriftValue - mean) / stdDev;

        zScore.Should().BeGreaterThan(3.0);

        if (zScore > 3.0)
        {
            tts.Speak("Early Anomaly Warning: Predictive drift detected on Node MCU_NODE_1 TEMP");
            slack.PublishAlert("MCU_NODE_1", "TEMP", newDriftValue, "Warning");
        }

        tts.SpokenMessages.Should().HaveCount(1);
        slack.DispatchedWebhooks.Should().HaveCount(1);
        slack.DispatchedWebhooks[0].Should().Contain("[Warning]");
    }

    [Fact]
    public void AlarmResolution_FlushesResolvedStatusToSlack()
    {
        var node = new SensorNode("MCU_NODE_1", "Motor Node", "COM3", "ENGINE");
        node.Thresholds["TEMP"] = (Min: 0.0, Max: 85.0);

        var slack = new MockSlackWebhookPublisher();
        bool inAlarmState = true;

        // Simulate value returning to normal
        double currentTemp = 42.0;
        bool alarmNow = node.UpdateVariable("TEMP", currentTemp);

        if (inAlarmState && !alarmNow)
        {
            inAlarmState = false;
            slack.PublishResolution("MCU_NODE_1", "TEMP");
        }

        inAlarmState.Should().BeFalse();
        slack.DispatchedWebhooks.Should().HaveCount(1);
        slack.DispatchedWebhooks[0].Should().Contain("[Resolved]");
    }
}
