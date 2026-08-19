using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Infrastructure.Integrations;
using TelemetryDashboard.Infrastructure.Replay;
using TelemetryDashboard.Infrastructure.Plugins;
using TelemetryDashboard.Infrastructure.Serial;
using TelemetryDashboard.Core.Protocols;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Recording;

namespace TelemetryDashboard.Tests;

public class IdeaFeaturesEmpiricalVerificationTests
{
    [Fact]
    public void Test_AdaptiveSamplingController_BurstModeSwitching()
    {
        var controller = new AdaptiveSamplingController
        {
            BaseRateHz = 5,
            BurstRateHz = 500,
            AnomalyThresholdSigma = 2.5,
            CooldownDurationSec = 0
        };

        // Initial rate is BaseRateHz
        Assert.Equal(5, controller.GetSamplingRate("temp_ch1"));

        // High Z-Score triggers Burst mode
        int newRate = controller.EvaluateSamplingRate("temp_ch1", 3.2);
        Assert.Equal(500, newRate);
        Assert.Equal(500, controller.GetSamplingRate("temp_ch1"));

        // Normal Z-Score reverts to Base mode
        int normalRate = controller.EvaluateSamplingRate("temp_ch1", 0.5);
        Assert.Equal(5, normalRate);
    }

    [Fact]
    public void Test_LlmDiagnosisAgent_EmergencyTriggersAndQuery()
    {
        var agent = new LlmDiagnosisAgent();

        bool triggered = agent.EvaluateEmergencyTriggers("temp", 4.0, out string command);
        Assert.True(triggered);
        Assert.Contains("SAFE_MODE", command);

        var anomalies = new[]
        {
            new AnomalyResult { ChannelName = "temp", CurrentValue = 105.4, ZScore = 3.8, IsAnomaly = true, PredictedValueIn60s = 120.0, EstimatedTimeToBreachSec = 15.0 }
        };

        var report = agent.ProcessNaturalLanguageQuery("최근 온도 이상 원인 분석해줘", anomalies);
        Assert.NotNull(report);
        Assert.Contains("temp", report.SummaryDiagnosis);
        Assert.Single(report.CriticalEvents);
    }

    [Fact]
    public void Test_IndustrialProtocolBridge_CANAndModbusConversion()
    {
        var bridge = new IndustrialProtocolBridge("CANbus_Modbus_ROS2");

        // CAN frame mock payload (8 bytes)
        byte[] canPayload = new byte[10];
        canPayload[0] = 0x08;
        canPayload[1] = 0x00;
        BitConverter.GetBytes((uint)0x123).CopyTo(canPayload, 2);
        BitConverter.GetBytes((float)87.5f).CopyTo(canPayload, 6);

        byte[] jsonBytes = bridge.ConvertToStandardPacket(canPayload);
        string jsonText = System.Text.Encoding.UTF8.GetString(jsonBytes);

        Assert.Contains("CANbus", jsonText);
        Assert.Contains("0x123", jsonText);
    }

    [Fact]
    public void Test_TimeTravelDvrPlayerAndIncidentReport()
    {
        var dvr = new TimeTravelDvrPlayer();

        // The analyzer id is what makes these frames carry a verdict. Without it the report treats
        // them as unexamined and lists the channel under exclusions — which still satisfies the
        // "temp_node1 appears" assertion below, but for the opposite reason to the one intended.
        const string analyzerId = "test-fixture/zscore";
        dvr.RecordFrame("temp_node1", 25.0, 0.2, false, analyzerId);
        dvr.RecordFrame("temp_node1", 98.4, 3.9, true, analyzerId);

        Assert.True(dvr.MaxDurationSec >= 0);

        var snapshot = dvr.ExtractSnapshot(DateTime.UtcNow.Ticks / 10_000_000.0, 60.0);
        Assert.NotEmpty(snapshot);

        var reportGen = new IncidentReportGenerator();
        string reportMd = reportGen.GenerateMarkdownReport("UPS Thermal Overheat", snapshot, "AI Diagnosis: Overheat detected");

        Assert.Contains("Incident Report", reportMd);
        Assert.Contains("temp_node1", reportMd);
    }

    [Fact]
    public async Task Test_EdgeMcuOtaFlasher_SimulatedFlashing()
    {
        var flasher = new EdgeMcuOtaFlasher();
        string tempFile = Path.GetTempFileName();
        await File.WriteAllBytesAsync(tempFile, new byte[1024]); // 1KB test binary

        var result = await flasher.FlashFirmwareAsync("COM3", tempFile, async (chunk) =>
        {
            await Task.Delay(1);
            return true; // ACK
        });

        Assert.True(result.Success);
        Assert.Equal(1024, result.BytesSent);
        Assert.Equal(1024, result.TotalBytes);
        if (File.Exists(tempFile)) File.Delete(tempFile);
    }

    [Fact]
    public void Test_HotReloadPluginSandbox_Monitoring()
    {
        var sandbox = new HotReloadPluginSandbox();
        string tempDir = Path.Combine(Path.GetTempPath(), "TelemetryPlugins_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        string pluginFile = Path.Combine(tempDir, "filter.cs");
        File.WriteAllText(pluginFile, "// Custom Filter Script");

        sandbox.StartMonitoring(tempDir);
        object res = sandbox.ExecuteFilter("Filter", "Packet");
        Assert.NotNull(res);

        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task Test_P2PMeshClusterSync_StartsBroadcastsAndStopsCleanly()
    {
        using var meshHub1 = new TelemetryDashboard.Infrastructure.Network.P2PMeshClusterSync { LocalHubName = "Hub-A" };
        using var meshHub2 = new TelemetryDashboard.Infrastructure.Network.P2PMeshClusterSync { LocalHubName = "Hub-B" };

        await meshHub1.StartAsync(9091);
        await meshHub2.StartAsync(9092);

        Assert.True(meshHub1.IsRunning);
        Assert.True(meshHub2.IsRunning);

        // No delivery assertion, deliberately. BroadcastSyncPacketAsync sends to the sender's own
        // ListenPort, so a cluster only converses when every member shares one port — these two
        // are on 9091 and 9092 and can never hear each other. The previous version of this test
        // subscribed to PacketReceived, set a flag, and asserted nothing, so it read as coverage of
        // mesh broadcast while covering only that neither call throws. That is what it verifies.
        await meshHub1.BroadcastSyncPacketAsync("ANOMALY_SYNC", new { Channel = "temp_ch1", ZScore = 4.2 });
        await Task.Delay(100);

        meshHub1.IsRunning.Should().BeTrue();
        meshHub2.IsRunning.Should().BeTrue();

        await meshHub1.StopAsync();
        await meshHub2.StopAsync();

        meshHub1.IsRunning.Should().BeFalse();
        meshHub2.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Test_WebRtcDataBridge_OfferAnswerNegotiation()
    {
        var bridge = new TelemetryDashboard.Infrastructure.Network.WebRtcTelemetryBridge();
        var offer = await bridge.CreateOfferAsync("client_browser_1");

        Assert.NotNull(offer);
        Assert.Equal("offer", offer.Type);

        // A real ICE/DTLS stack produced this SDP, so it carries an actual certificate
        // fingerprint and ICE credentials rather than a hand-written constant string.
        Assert.Contains("a=fingerprint:", offer.Sdp);
        Assert.Contains("a=ice-ufrag:", offer.Sdp);
        Assert.Contains("application", offer.Sdp);

        // The peer exists once negotiation starts, but its data channel cannot be open until a
        // genuine remote peer completes ICE and DTLS. Reporting an open channel here is exactly
        // the false signal the previous stub produced.
        Assert.Equal(1, bridge.RegisteredPeerCount);
        Assert.Equal(0, bridge.ActiveDataChannelCount);

        await bridge.DisposeAsync();
    }

    [Fact]
    public void Test_TelemetryCsvRecorder_RealDiskWritingAndFlushing()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "TelemetryCsvTest_" + Guid.NewGuid());
        using var recorder = new TelemetryCsvRecorder();

        string savedPath = recorder.StartRecording(tempDir, "test_real_telemetry.csv");
        Assert.True(recorder.IsRecording);
        Assert.True(File.Exists(savedPath));

        // Record 25 real telemetry samples
        for (int i = 0; i < 25; i++)
        {
            recorder.RecordSample("COM3", "Temperature", 25.0 + i * 0.5, 0.5, false, 30.0);
            recorder.RecordSample("COM3", "Vibration", 0.2 + i * 0.01, 0.2, false, 0.25);
        }

        string finalPath = recorder.StopRecording();
        Assert.False(recorder.IsRecording);
        Assert.True(File.Exists(finalPath));
        Assert.Equal(50, recorder.RecordedPacketCount);
        Assert.True(recorder.FileSizeBytes > 500);

        string[] allLines = File.ReadAllLines(finalPath);
        Assert.Equal(51, allLines.Length); // 1 header + 50 data rows
        Assert.Contains("Timestamp_ISO,Timestamp_Sec,NodeId,Channel", allLines[0]);
        Assert.Contains("Temperature", allLines[1]);

        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task Test_LlmDiagnosisAgent_ProcessQueryWithLlmApiAsync_OfflineFallback()
    {
        var agent = new LlmDiagnosisAgent();
        var anomalies = new[]
        {
            new AnomalyResult { ChannelName = "temp_fet", CurrentValue = 104.5, ZScore = 4.2, IsAnomaly = true, PredictedValueIn60s = 125.0, EstimatedTimeToBreachSec = 10.0 }
        };

        var config = new LlmApiConfig { Provider = "Offline" };
        var report = await agent.ProcessQueryWithLlmApiAsync("온도 스파이크 원인 분석", anomalies, config);

        Assert.NotNull(report);
        Assert.Equal("CRITICAL", report.SeverityLevel);
        Assert.Contains("temp_fet", report.MarkdownReport);
        Assert.Contains("Root Cause", report.MarkdownReport);
    }
}


