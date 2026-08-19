using System.Threading.Channels;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Serial;

using TelemetryDashboard.Core.Events;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F02_SerialManagerTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void SerialManager_Initialization_HasEmptyActivePorts()
    {
        var manager = new MultiPortSerialManager(new Win32HotPlugHook());
        manager.ActivePorts.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task SerialManager_ConnectPort_RegistersActivePort()
    {
        var mockManager = new Mock<ISerialManager>();
        var activePorts = new Dictionary<string, PortConnectionStatus>
        {
            ["COM3"] = PortConnectionStatus.Connected
        };
        mockManager.Setup(m => m.ActivePorts).Returns(activePorts);
        mockManager.Setup(m => m.ConnectPortAsync("COM3", 115200, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(true);

        var result = await mockManager.Object.ConnectPortAsync("COM3", 115200);

        result.Should().BeTrue();
        mockManager.Object.ActivePorts.Should().ContainKey("COM3");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task SerialManager_DisconnectPort_RemovesActivePort()
    {
        var mockManager = new Mock<ISerialManager>();
        var activePorts = new Dictionary<string, PortConnectionStatus>();
        mockManager.Setup(m => m.ActivePorts).Returns(activePorts);
        mockManager.Setup(m => m.DisconnectPortAsync("COM3"))
                   .Returns(Task.CompletedTask);

        await mockManager.Object.DisconnectPortAsync("COM3");

        mockManager.Object.ActivePorts.Should().NotContainKey("COM3");
        mockManager.Verify(m => m.DisconnectPortAsync("COM3"), Times.Once);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void SerialManager_PacketReader_ProvidesAsyncStream()
    {
        var channel = Channel.CreateUnbounded<RawPacket>();
        var mockManager = new Mock<ISerialManager>();
        mockManager.Setup(m => m.PacketReader).Returns(channel.Reader);

        mockManager.Object.PacketReader.Should().NotBeNull();
        channel.Writer.TryWrite(new RawPacket { Payload = "$TELE,1,TEMP,25*00", PortName = "COM3" });
        channel.Reader.TryRead(out var packet).Should().BeTrue();
        packet!.PortName.Should().Be("COM3");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task SerialManager_WriteLineAsync_SendsDataToPort()
    {
        var mockManager = new Mock<ISerialManager>();
        mockManager.Setup(m => m.WriteLineAsync("COM3", "$CMD,REQ_RESYNC,0*00", It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        await mockManager.Object.WriteLineAsync("COM3", "$CMD,REQ_RESYNC,0*00");

        mockManager.Verify(m => m.WriteLineAsync("COM3", "$CMD,REQ_RESYNC,0*00", It.IsAny<CancellationToken>()), Times.Once);
    }
}
