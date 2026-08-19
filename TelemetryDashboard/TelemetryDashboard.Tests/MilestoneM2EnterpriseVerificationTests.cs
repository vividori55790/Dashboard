using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Infrastructure.Replay;
using TelemetryDashboard.Core.Streaming;
using TelemetryDashboard.Core.Recording;

namespace TelemetryDashboard.Tests;

/// <summary>M2 enterprise-feature verification against the portable layers.</summary>
/// <remarks>
/// <c>Test_EventLogEntry_PropertiesAndAlertLevels</c> moved to TelemetryDashboard.Tests.Desktop:
/// <c>EventLogEntry</c> is declared inside <c>ControlPanelControl.xaml.cs</c>, so it was the single
/// reason this otherwise Core-only file forced a WPF reference.
/// </remarks>
public class MilestoneM2EnterpriseVerificationTests
{
    [Fact]
    public void Test_DashboardExporter_DefaultLayout_GeneratesValidHtmlWithSdk()
    {
        var exporter = new DashboardExporter();
        string tempFile = Path.Combine(Path.GetTempPath(), $"test_dash_{Guid.NewGuid():N}.html");

        try
        {
            string resultPath = exporter.ExportCustomHtmlDashboard(tempFile, "Dual Active Bridge Monitoring Hub");
            resultPath.Should().Be(tempFile);
            File.Exists(tempFile).Should().BeTrue();

            string htmlContent = File.ReadAllText(tempFile);
            htmlContent.Should().Contain("Dual Active Bridge Monitoring Hub");
            htmlContent.Should().Contain("telemetry-client.js");
            htmlContent.Should().Contain("dashboard-container");
            htmlContent.Should().Contain("w-temp");
            htmlContent.Should().Contain("w-zscore");
            htmlContent.Should().Contain("w-chart");
            htmlContent.Should().Contain("w-vin");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Test_DashboardExporter_CustomWidgetsSchema_SerializesAllFourWidgetTypes()
    {
        var exporter = new DashboardExporter();
        string tempFile = Path.Combine(Path.GetTempPath(), $"custom_schema_{Guid.NewGuid():N}.html");

        var customWidgets = new List<WidgetConfig>
        {
            new WidgetConfig { Id = "w1", WidgetType = "digital_card", Title = "Output Current", Field = "iout", Unit = "A", ColorTheme = "#00FF66" },
            new WidgetConfig { Id = "w2", WidgetType = "gauge_meter", Title = "Input Voltage", Field = "vin", Unit = "V", MinLimit = 0, MaxLimit = 600, ColorTheme = "#66FCF1" },
            new WidgetConfig { Id = "w3", WidgetType = "zscore_card", Title = "Anomaly Z-Score", Field = "anomalyScore", Unit = "σ", ColorTheme = "#FF2E63" },
            new WidgetConfig { Id = "w4", WidgetType = "line_chart", Title = "Live Temp Trend", Field = "temp", Unit = "°C", ColorTheme = "#BA68C8" }
        };

        try
        {
            exporter.ExportCustomHtmlDashboard(tempFile, "Custom Grid Dashboard", customWidgets);
            File.Exists(tempFile).Should().BeTrue();

            string htmlContent = File.ReadAllText(tempFile);
            htmlContent.Should().Contain("Output Current");
            htmlContent.Should().Contain("Input Voltage");
            htmlContent.Should().Contain("Anomaly Z-Score");
            htmlContent.Should().Contain("Live Temp Trend");
            htmlContent.Should().Contain("#FF2E63");
            htmlContent.Should().Contain("#BA68C8");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Test_TimeTravelDvrPlayer_RecordAndExtractSnapshot()
    {
        var dvr = new TimeTravelDvrPlayer();
        dvr.RecordFrame("DAB_CH1", 45.2, 0.4, false);
        dvr.RecordFrame("DAB_CH2", 52.8, 1.2, false);
        dvr.RecordFrame("PSFB_CH1", 102.5, 3.8, true);

        dvr.MaxDurationSec.Should().BeGreaterThanOrEqualTo(0);

        double centerSec = DateTime.UtcNow.Ticks / 10_000_000.0;
        var snapshot = dvr.ExtractSnapshot(centerSec, 60.0);
        snapshot.Count.Should().Be(3);

        var anomaly = snapshot.FirstOrDefault(f => f.IsAnomaly);
        anomaly.Should().NotBeNull();
        anomaly!.ChannelName.Should().Be("PSFB_CH1");
        anomaly.ZScore.Should().Be(3.8);
    }

    [Fact]
    public void Test_TimeTravelDvrPlayer_SeekAndFrameReplayedEvent()
    {
        var dvr = new TimeTravelDvrPlayer();
        dvr.RecordFrame("CH1", 10.0, 0.1, false);
        dvr.RecordFrame("CH1", 20.0, 0.2, false);
        dvr.RecordFrame("CH1", 30.0, 0.3, false);

        DvrFrame? replayed = null;
        dvr.FrameReplayed += (sender, args) => replayed = args.Frame;

        dvr.SeekTo(0.0);
        replayed.Should().NotBeNull();
        replayed!.ChannelName.Should().Be("CH1");

        dvr.Play();
        dvr.IsPlaying.Should().BeTrue();
        dvr.Pause();
        dvr.IsPlaying.Should().BeFalse();

        dvr.PlaybackSpeed = 5.0;
        dvr.PlaybackSpeed.Should().Be(5.0);
    }

    /// <summary>
    /// Analyzer identity for hand-built fixture frames.
    /// </summary>
    /// <remarks>
    /// A <see cref="DvrFrame"/> with a z-score but no <c>AnalyzerId</c> is, by contract, a frame no
    /// analyzer examined — the report excludes it rather than treating an unset default as a
    /// judgement. These fixtures represent frames that <em>were</em> scored, so they must say so.
    /// </remarks>
    private const string FixtureAnalyzerId = "test-fixture/zscore";

    [Fact]
    public void Test_IncidentReportGenerator_GeneratesDetailedMarkdown()
    {
        var generator = new IncidentReportGenerator();
        var anomalyFrames = new List<DvrFrame>
        {
            new DvrFrame { TimestampSec = 100.1, ChannelName = "DAB_TEMP", Value = 108.5, ZScore = 4.2, IsAnomaly = true, AnalyzerId = FixtureAnalyzerId },
            new DvrFrame { TimestampSec = 100.2, ChannelName = "PSFB_VIB", Value = 3.4, ZScore = 3.9, IsAnomaly = true, AnalyzerId = FixtureAnalyzerId },
            new DvrFrame { TimestampSec = 100.3, ChannelName = "DAB_VIN", Value = 380.0, ZScore = 0.5, IsAnomaly = false, AnalyzerId = FixtureAnalyzerId }
        };

        string markdown = generator.GenerateMarkdownReport("PSFB Thermal Overload Event", anomalyFrames, "Primary inverter temperature exceeded 105°C threshold.");

        markdown.Should().Contain("# 🚨 Telemetry Incident Report — PSFB Thermal Overload Event");
        markdown.Should().Contain("DAB_TEMP");
        markdown.Should().Contain("108.50");
        markdown.Should().Contain("4.20σ");
        markdown.Should().Contain("🚨 CRITICAL");
        // Observations are derived from the frames; the checklist that follows is generic and is
        // now labelled as such rather than presented as this incident's root cause.
        markdown.Should().Contain("데이터 기반 관측 결과");
        markdown.Should().Contain("최대 편차 채널");
        markdown.Should().Contain("일반 점검 체크리스트");
        markdown.Should().Contain("서킷 브레이커");
    }

    [Fact]
    public void Test_IncidentReportGenerator_SaveToFile()
    {
        var generator = new IncidentReportGenerator();
        string tempDir = Path.Combine(Path.GetTempPath(), $"incidents_{Guid.NewGuid():N}");
        string reportContent = "# Test Incident Report\nContent";

        try
        {
            string savedPath = generator.SaveReportToFile(reportContent, tempDir, "TestReport.md");
            File.Exists(savedPath).Should().BeTrue();
            File.ReadAllText(savedPath).Should().Be(reportContent);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Test_TelemetryStreamingServer_DvrBufferIntegration()
    {
        var server = new TelemetryStreamingServer(8091);
        server.Port.Should().Be(8091);
        server.DvrPlayer.Should().NotBeNull();

        var packet = new { device = "DAB_CONVERTER", temp = 65.4, anomalyScore = 3.9 };
        server.BroadcastTelemetry(packet);

        double nowSec = DateTime.UtcNow.Ticks / 10_000_000.0;
        var snapshot = server.DvrPlayer.ExtractSnapshot(nowSec, 10.0);
        snapshot.Should().NotBeEmpty();

        // Channels are recorded as "<node>.<field>". Naming the channel after the node alone
        // cannot represent a frame carrying more than one measurement, which every real frame does.
        snapshot[0].ChannelName.Should().Be("DAB_CONVERTER.temp");
        snapshot[0].Value.Should().Be(65.4);
        snapshot[0].ZScore.Should().Be(3.9);
        snapshot[0].IsAnomaly.Should().BeTrue();
    }

    [Fact]
    public void Test_TimeTravelDvrPlayer_GetFramesInRange_AndClear()
    {
        var dvr = new TimeTravelDvrPlayer();
        dvr.RecordFrame("CH1", 10.0, 0.1, false);
        dvr.RecordFrame("CH2", 20.0, 0.2, false);
        dvr.RecordFrame("CH3", 30.0, 0.3, false);

        double start = DateTime.UtcNow.Ticks / 10_000_000.0 - 10.0;
        double end = start + 20.0;

        var frames = dvr.GetFramesInRange(start, end);
        frames.Count.Should().Be(3);

        dvr.Clear();
        dvr.MaxDurationSec.Should().Be(0.0);
        dvr.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public void Test_IncidentReportGenerator_NominalCase_OutputsNormalRow()
    {
        var generator = new IncidentReportGenerator();
        var emptyList = new List<DvrFrame>();

        string md = generator.GenerateMarkdownReport("Nominal Test", emptyList, "");
        md.Should().Contain("# 🚨 Telemetry Incident Report — Nominal Test");
        md.Should().Contain("정상");
    }

    [Fact]
    public void Test_TelemetryStreamingServer_StartStop_GracefulLifecycle()
    {
        var server = new TelemetryStreamingServer(8099);
        server.IsRunning.Should().BeFalse();

        server.Start("non_existent_path.html");
        server.IsRunning.Should().BeTrue();
        server.ConnectedClientCount.Should().Be(0);

        server.Stop();
        server.IsRunning.Should().BeFalse();
    }
}
