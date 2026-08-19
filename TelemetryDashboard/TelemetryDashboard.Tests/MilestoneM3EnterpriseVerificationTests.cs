using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Infrastructure.Integrations;
using TelemetryDashboard.Core.Analytics;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Enterprise Verification Test Suite for Milestone M3:
/// - Component 1: Adaptive Dynamic Sampling State Machine (1Hz Nominal - 1000Hz Burst, Hysteresis, Decimation)
/// - Component 2: Multi-Channel Alert Forwarder (Slack Block Kit, Discord Embeds, Telegram, Webhook, Sparklines, Throttling)
/// - Component 3: LLM Natural Language Diagnosis Agent (EN/KO Query Parser, Multi-Channel Cross-Correlation, Confidence)
/// - Component 4: Emergency MCU Control (Z > 3.5 Trigger, Safety Arming Interlock, Emergency Stop, Serial Dispatch)
/// </summary>
public class MilestoneM3EnterpriseVerificationTests
{
    #region Component 1: Adaptive Dynamic Sampling State Machine Tests

    [Fact]
    public void AdaptiveSampling_NominalRate_InitializesTo1Hz()
    {
        var controller = new AdaptiveSamplingController();

        controller.BaseRateHz.Should().Be(1);
        controller.BurstRateHz.Should().Be(1000);
        controller.AnomalyThresholdSigma.Should().Be(2.5);
        controller.GetSamplingRate("node1_temp").Should().Be(1);
        controller.GetSamplingMode("node1_temp").Should().Be(SamplingMode.Nominal);
    }

    [Fact]
    public void AdaptiveSampling_ZScoreSpike_TransitionsTo1000HzBurstMode()
    {
        var controller = new AdaptiveSamplingController();
        bool eventFired = false;
        SamplingRateChangedEventArgs? eventArgs = null;

        controller.SamplingRateChanged += (_, args) =>
        {
            eventFired = true;
            eventArgs = args;
        };

        int newRate = controller.EvaluateSamplingRate("sensor_press", 3.2);

        newRate.Should().Be(1000);
        controller.GetSamplingRate("sensor_press").Should().Be(1000);
        controller.GetSamplingMode("sensor_press").Should().Be(SamplingMode.Burst);

        eventFired.Should().BeTrue();
        eventArgs.Should().NotBeNull();
        eventArgs!.ChannelId.Should().Be("sensor_press");
        eventArgs.OldRateHz.Should().Be(1);
        eventArgs.NewRateHz.Should().Be(1000);
        eventArgs.Mode.Should().Be(SamplingMode.Burst);
    }

    [Fact]
    public void AdaptiveSampling_HysteresisCooldown_MaintainsBurstUntilCooldownExpires()
    {
        var controller = new AdaptiveSamplingController
        {
            BaseRateHz = 1,
            BurstRateHz = 1000,
            CooldownDurationSec = 5.0,
            MinBurstDurationSec = 2.0
        };

        var t0 = new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

        // 1. Initial anomaly triggers Burst mode
        int burstRate = controller.EvaluateSamplingRate("vib_ch1", 3.5, t0);
        burstRate.Should().Be(1000);
        controller.GetSamplingMode("vib_ch1").Should().Be(SamplingMode.Burst);

        // 2. Normal sample at t0 + 1s (within MinBurstDuration and Cooldown) -> transitions to Cooldown, remains 1000Hz
        int cooldownRate1 = controller.EvaluateSamplingRate("vib_ch1", 0.8, t0.AddSeconds(1.0));
        cooldownRate1.Should().Be(1000);
        controller.GetSamplingMode("vib_ch1").Should().Be(SamplingMode.Cooldown);

        // 3. Normal sample at t0 + 3s (still within 5s Cooldown) -> remains Cooldown, 1000Hz
        int cooldownRate2 = controller.EvaluateSamplingRate("vib_ch1", 0.4, t0.AddSeconds(3.0));
        cooldownRate2.Should().Be(1000);
        controller.GetSamplingMode("vib_ch1").Should().Be(SamplingMode.Cooldown);

        // 4. Normal sample at t0 + 6s (Cooldown expired) -> transitions back to Nominal, 1Hz
        int nominalRate = controller.EvaluateSamplingRate("vib_ch1", 0.2, t0.AddSeconds(6.0));
        nominalRate.Should().Be(1);
        controller.GetSamplingMode("vib_ch1").Should().Be(SamplingMode.Nominal);
    }

    [Fact]
    public void AdaptiveSampling_DecimationFilter_SkipsSamplesInNominalMode()
    {
        var controller = new AdaptiveSamplingController
        {
            BaseRateHz = 1,
            BurstRateHz = 1000
        };

        // In nominal mode (1Hz), skip factor = 1000 / 1 = 1000
        controller.ShouldSample("temp_1", 0).Should().BeTrue();
        controller.ShouldSample("temp_1", 1).Should().BeFalse();
        controller.ShouldSample("temp_1", 500).Should().BeFalse();
        controller.ShouldSample("temp_1", 999).Should().BeFalse();
        controller.ShouldSample("temp_1", 1000).Should().BeTrue();

        // Switch to burst mode (1000Hz) -> every sample is accepted
        controller.EvaluateSamplingRate("temp_1", 3.0);
        controller.ShouldSample("temp_1", 1).Should().BeTrue();
        controller.ShouldSample("temp_1", 2).Should().BeTrue();
        controller.ShouldSample("temp_1", 3).Should().BeTrue();
    }

    [Fact]
    public void AdaptiveSampling_FormatRateCommand_GeneratesMcuString()
    {
        var controller = new AdaptiveSamplingController();

        string cmdWithNode = controller.FormatRateCommand("NODE_2", 1000);
        cmdWithNode.Should().Be("$CMD,RATE,NODE_2,1000\n");

        string cmdGeneric = controller.FormatRateCommand("", 500);
        cmdGeneric.Should().Be("$CMD,RATE,500\n");
    }

    #endregion

    #region Component 2: Multi-Channel Alert Forwarder & Waveform Snapshot Tests

    [Fact]
    public void WaveformSnapshot_GenerateAsciiSparkline_ProducesValidUnicodeBlocks()
    {
        var linearSamples = new double[] { 0, 10, 20, 30, 40, 50, 60, 70 };
        string sparkline = WaveformSnapshotGenerator.GenerateAsciiSparkline(linearSamples);

        sparkline.Should().Be(" ▂▃▄▅▆▇█");
        sparkline.Length.Should().Be(8);

        // Empty handling
        WaveformSnapshotGenerator.GenerateAsciiSparkline(Array.Empty<double>()).Should().BeEmpty();
    }

    [Fact]
    public void WaveformSnapshot_GenerateSvgWaveform_ProducesValidXmlPolyline()
    {
        var samples = new double[] { 10.0, 50.0, 20.0, 90.0, 15.0 };
        string svg = WaveformSnapshotGenerator.GenerateSvgWaveform(samples, 300, 60, "#FF2E63");

        svg.Should().StartWith("<svg xmlns=\"http://www.w3.org/2000/svg\"");
        svg.Should().Contain("viewBox=\"0 0 300 60\"");
        svg.Should().Contain("<polyline");
        svg.Should().Contain("stroke=\"#FF2E63\"");
        svg.Should().EndWith("</svg>");

        var stats = WaveformSnapshotGenerator.ComputeStats(samples);
        stats.Min.Should().Be(10.0);
        stats.Max.Should().Be(90.0);
        stats.PeakToPeak.Should().Be(80.0);
        stats.Count.Should().Be(5);
    }

    [Fact]
    public void MultiChannelAlertForwarder_SlackBlockKit_FormatsHeaderAndSectionBlocks()
    {
        var forwarder = new MultiChannelAlertForwarder();
        var anomaly = new AnomalyResult
        {
            ChannelName = "DAB_CH1_TEMP",
            CurrentValue = 104.5,
            Mean = 42.0,
            StdDev = 15.2,
            ZScore = 4.11,
            IsAnomaly = true,
            EstimatedTimeToBreachSec = 12.4
        };

        string json = forwarder.FormatSlackPayload(anomaly, "Thermal runaway impending", " ▂▃▅██");
        json.Should().NotBeNullOrWhiteSpace();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("blocks", out var blocks).Should().BeTrue();
        blocks.GetArrayLength().Should().BeGreaterOrEqualTo(3);

        // Validate header block
        var header = blocks[0];
        header.GetProperty("type").GetString().Should().Be("header");
        header.GetProperty("text").GetProperty("text").GetString().Should().Contain("DAB_CH1_TEMP");

        // Validate section with fields
        string jsonText = json;
        jsonText.Should().Contain("4.11σ");
        jsonText.Should().Contain("104.50");
        jsonText.Should().Contain("12.4s");
    }

    [Fact]
    public void MultiChannelAlertForwarder_DiscordEmbeds_FormatsColorAndFields()
    {
        var forwarder = new MultiChannelAlertForwarder();
        var anomaly = new AnomalyResult
        {
            ChannelName = "PSFB_VOUT",
            CurrentValue = 18.2,
            ZScore = 3.6,
            IsAnomaly = true,
            EstimatedTimeToBreachSec = 8.0
        };

        string json = forwarder.FormatDiscordPayload(anomaly, "Voltage drop observed", "█▇▅▃▂ ");
        json.Should().NotBeNullOrWhiteSpace();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("embeds", out var embeds).Should().BeTrue();
        var embed = embeds[0];
        embed.GetProperty("title").GetString().Should().Contain("PSFB_VOUT");
        embed.GetProperty("color").GetInt32().Should().Be(16723555); // Red for Z >= 3.0
        embed.GetProperty("fields").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public void MultiChannelAlertForwarder_GenericWebhook_PostsStandardJsonSchema()
    {
        var forwarder = new MultiChannelAlertForwarder();
        var anomaly = new AnomalyResult
        {
            ChannelName = "VIB_MOTOR_1",
            CurrentValue = 88.5,
            Mean = 20.0,
            StdDev = 18.0,
            ZScore = 3.8,
            IsAnomaly = true,
            EstimatedTimeToBreachSec = 5.5
        };

        var stats = new WaveformStats { Min = 15.0, Max = 88.5, Mean = 35.0, PeakToPeak = 73.5, StdDev = 18.0, Count = 50 };
        string json = forwarder.FormatGenericWebhookPayload(anomaly, "Dynamic resonance spike", "  ▃▅▇█", "<svg></svg>", stats);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("severity").GetString().Should().Be("CRITICAL");
        root.GetProperty("channelName").GetString().Should().Be("VIB_MOTOR_1");
        root.GetProperty("zScore").GetDouble().Should().Be(3.8);
        root.GetProperty("sparkline").GetString().Should().Be("  ▃▅▇█");
        root.GetProperty("stats").GetProperty("peakToPeak").GetDouble().Should().Be(73.5);
    }

    [Fact]
    public async Task MultiChannelAlertForwarder_AlertThrottler_SuppressesDuplicateAlerts()
    {
        var t0 = new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

        // Pin the forwarder's clock to the simulated timeline. Without this the dispatch path
        // stamps throttles with the real wall clock, so this test only passed on the day it
        // was written and silently rotted afterwards.
        var forwarder = new MultiChannelAlertForwarder(config: new AlertChannelConfig
        {
            ThrottleCooldownSec = 15.0,
            MinZScoreJumpForBypass = 1.5
        }, utcNow: () => t0);

        // 1. Initial alert is allowed
        forwarder.ShouldThrottle("temp_node", 3.0, t0).Should().BeFalse();

        // 2. Dispatch simulated at t0
        await forwarder.DispatchAlertAsync(new AnomalyResult { ChannelName = "temp_node", ZScore = 3.0 });

        // 3. Duplicate alert within 5s and similar Z-Score is throttled
        forwarder.ShouldThrottle("temp_node", 3.2, t0.AddSeconds(5.0)).Should().BeTrue();

        // 4. Large Z-Score jump (delta Z = 2.0 >= 1.5) bypasses throttle
        forwarder.ShouldThrottle("temp_node", 5.0, t0.AddSeconds(6.0)).Should().BeFalse();

        // 5. Alert after cooldown window (16s) is allowed
        forwarder.ShouldThrottle("temp_node", 3.1, t0.AddSeconds(16.0)).Should().BeFalse();
    }

    #endregion

    #region Component 3: LLM Natural Language Diagnosis Agent Tests

    [Fact]
    public void LlmDiagnosisAgent_EnglishQuery_AnalyzesVibrationSpike()
    {
        var agent = new LlmDiagnosisAgent();
        var anomalies = new[]
        {
            new AnomalyResult
            {
                ChannelName = "VIB_NODE_2",
                CurrentValue = 92.4,
                Mean = 15.0,
                StdDev = 18.2,
                ZScore = 4.25,
                IsAnomaly = true,
                PredictedValueIn60s = 135.0,
                EstimatedTimeToBreachSec = 7.5
            }
        };

        var report = agent.ProcessNaturalLanguageQuery("Why is vibration spiking on VIB_NODE_2?", anomalies);

        report.Should().NotBeNull();
        report.TargetChannel.Should().Be("VIB_NODE_2");
        report.SeverityLevel.Should().Be("CRITICAL");
        report.ConfidenceScore.Should().BeGreaterThan(0.80);
        report.RootCause.Should().ContainEquivalentOf("vibration");
        report.MarkdownReport.Should().Contain("Telemetry AI Diagnostic Report");
        report.MarkdownReport.Should().Contain("VIB_NODE_2");
    }

    [Fact]
    public void LlmDiagnosisAgent_KoreanQuery_AnalyzesThermalOverload()
    {
        var agent = new LlmDiagnosisAgent();
        var anomalies = new[]
        {
            new AnomalyResult
            {
                ChannelName = "PSFB_TEMP",
                CurrentValue = 108.2,
                Mean = 45.0,
                StdDev = 16.0,
                ZScore = 3.95,
                IsAnomaly = true,
                PredictedValueIn60s = 130.0,
                EstimatedTimeToBreachSec = 14.2
            }
        };

        var report = agent.ProcessNaturalLanguageQuery("PSFB_TEMP 온도 급상승 원인 및 예상 시간 알려줘", anomalies);

        report.Should().NotBeNull();
        report.TargetChannel.Should().Be("PSFB_TEMP");
        report.SummaryDiagnosis.Should().Contain("PSFB_TEMP");
        report.TrendAnalysis.Should().Contain("14.2초");
        report.RecommendedAction.Should().Contain("냉각");
    }

    [Fact]
    public void LlmDiagnosisAgent_MultiChannelCorrelation_IdentifiesRootCause()
    {
        var agent = new LlmDiagnosisAgent();
        var anomalies = new[]
        {
            new AnomalyResult { ChannelName = "MOTOR_TEMP", CurrentValue = 102.0, ZScore = 3.6, IsAnomaly = true },
            new AnomalyResult { ChannelName = "MOTOR_VIB", CurrentValue = 89.0, ZScore = 3.8, IsAnomaly = true }
        };

        var report = agent.ProcessNaturalLanguageQuery("모터 베어링 이상 징후 분석해줘", anomalies);

        report.Should().NotBeNull();
        // Cross-correlation should identify bearing degradation / friction
        report.RootCause.Should().Contain("베어링");
        report.ConfidenceScore.Should().BeGreaterThanOrEqualTo(0.90);
    }

    #endregion

    #region Component 4: Emergency MCU Control Tests

    [Fact]
    public void EmergencyMcuController_ZScoreAbove3Point5_TriggersEmergencyCommand()
    {
        var controller = new EmergencyMcuController();

        bool triggered = controller.EvaluateEmergencyTriggers("temp_ch1", 3.8, 105.0, out string command);

        triggered.Should().BeTrue();
        command.Should().Contain("SAFE_MODE");
    }

    [Fact]
    public void EmergencyMcuController_ZScoreBelow3Point5_DoesNotTrigger()
    {
        var controller = new EmergencyMcuController();

        bool triggered = controller.EvaluateEmergencyTriggers("temp_ch1", 2.8, 65.0, out string command);

        triggered.Should().BeFalse();
        command.Should().BeEmpty();
    }

    [Fact]
    public async Task EmergencyMcuController_DisarmedState_SuppressesCommandDispatch()
    {
        string dispatchedCommand = string.Empty;
        var controller = new EmergencyMcuController(dispatchCallback: (_, cmd) =>
        {
            dispatchedCommand = cmd;
            return Task.CompletedTask;
        });

        // Disarm safety interlock
        controller.Disarm();
        controller.IsArmed.Should().BeFalse();

        bool success = await controller.EvaluateAndDispatchAsync("COM3", "temp_main", 4.2, 115.0);

        success.Should().BeFalse();
        dispatchedCommand.Should().BeEmpty(); // Suppressed!

        controller.History.Should().HaveCount(1);
        controller.History[0].Dispatched.Should().BeFalse();
        controller.History[0].Reason.Should().Contain("Disarmed");
    }

    [Fact]
    public async Task EmergencyMcuController_EmergencyStopAll_DispatchesAcrossAllPorts()
    {
        var dispatchedList = new List<(string Port, string Cmd)>();
        var controller = new EmergencyMcuController(dispatchCallback: (port, cmd) =>
        {
            dispatchedList.Add((port, cmd));
            return Task.CompletedTask;
        });

        int count = await controller.EmergencyStopAllAsync("Operator Manual Scram");

        count.Should().Be(1);
        dispatchedList.Should().HaveCount(1);
        dispatchedList[0].Cmd.Should().Contain("EMERGENCY_STOP");
        controller.History.Should().ContainSingle(h => h.Reason == "Operator Manual Scram");
    }

    [Fact]
    public async Task EmergencyMcuController_DebounceCooldown_PreventsCommandFlooding()
    {
        int dispatchCount = 0;
        var controller = new EmergencyMcuController(dispatchCallback: (_, _) =>
        {
            dispatchCount++;
            return Task.CompletedTask;
        });

        // 1. First trigger dispatches
        bool first = await controller.EvaluateAndDispatchAsync("COM3", "overheat_ch", 4.0, 110.0);
        first.Should().BeTrue();
        dispatchCount.Should().Be(1);

        // 2. Immediate second trigger within CooldownSec (default 5.0s) is suppressed
        bool second = await controller.EvaluateAndDispatchAsync("COM3", "overheat_ch", 4.1, 112.0);
        second.Should().BeFalse();
        dispatchCount.Should().Be(1);

        // 3. Acknowledging emergency clears debounce and allows re-trigger
        controller.AcknowledgeEmergency("overheat_ch");
        bool third = await controller.EvaluateAndDispatchAsync("COM3", "overheat_ch", 4.2, 114.0);
        third.Should().BeTrue();
        dispatchCount.Should().Be(2);
    }

    #endregion
}
