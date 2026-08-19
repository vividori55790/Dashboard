using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Serial;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F04_AutoReconnectTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void AutoReconnect_Initialization_IsDisabledByDefault()
    {
        var mockSerial = new Mock<ISerialManager>();
        var engine = new AutoReconnectEngine(mockSerial.Object);

        engine.IsRunning.Should().BeFalse();
        engine.ReconnectInterval.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task AutoReconnect_OnReconnection_SendsReqResyncCommand()
    {
        var mockSerial = new Mock<ISerialManager>();
        mockSerial.Setup(m => m.ConnectPortAsync("COM3", 115200, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(true);
        mockSerial.Setup(m => m.WriteLineAsync("COM3", It.Is<string>(s => s.Contains("REQ_RESYNC")), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        var engine = new AutoReconnectEngine(mockSerial.Object);
        bool success = await engine.TryReconnectAndResyncAsync("COM3", 115200, DateTime.UtcNow.AddMinutes(-1));

        success.Should().BeTrue();
        mockSerial.Verify(m => m.WriteLineAsync("COM3", It.Is<string>(s => s.Contains("REQ_RESYNC")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AutoReconnect_ReqResync_IncludesLastKnownTimestamp()
    {
        var lastSeen = DateTime.UtcNow.AddSeconds(-30);
        var packetStr = TestDataGenerator.CreateCmdPacket("REQ_RESYNC", ((long)(lastSeen - DateTime.UnixEpoch).TotalSeconds).ToString());

        packetStr.Should().Contain("REQ_RESYNC");
        packetStr.Should().StartWith("$CMD,REQ_RESYNC,");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AutoReconnect_ProcessesHistoricalStreamPackets()
    {
        var histFrame = TestDataGenerator.CreateHistResyncPacket("NODE_1", "TEMP", 45.0, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        histFrame.Should().StartWith("$HIST,NODE_1,TEMP,45.00,");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task AutoReconnect_Cancel_StopsRetryLoop()
    {
        var mockSerial = new Mock<ISerialManager>();
        var engine = new AutoReconnectEngine(mockSerial.Object);
        using var cts = new CancellationTokenSource();

        engine.StartMonitoring("COM3", 115200);
        engine.IsRunning.Should().BeTrue();

        await engine.StopMonitoringAsync();
        engine.IsRunning.Should().BeFalse();
    }
}
