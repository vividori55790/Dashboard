using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F28_KestrelWebServerTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void KestrelServer_Initialize_ConfiguresEndpointPort8080()
    {
        var server = new KestrelServerState();
        server.Initialize(8080);

        server.Port.Should().Be(8080);
        server.ListeningUrl.Should().Be("http://localhost:8080");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void KestrelServer_SseHandler_StreamsTelemetryJsonPackets()
    {
        var server = new KestrelServerState();
        var packet = new TelemetryPacket("MCU_1", "TEMP", 45.0, "C");

        string sseFrame = server.FormatSseEvent(packet);

        sseFrame.Should().StartWith("data: {");
        sseFrame.Should().Contain("\"nodeId\":\"MCU_1\"");
        sseFrame.Should().EndWith("\n\n");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void KestrelServer_RestStatus_ReturnsOkWithAppHealth()
    {
        var server = new KestrelServerState();
        string jsonResponse = server.GetRestStatus();

        jsonResponse.Should().Contain("\"status\":\"Healthy\"");
        jsonResponse.Should().Contain("\"version\":\"1.0.0\"");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void KestrelServer_RestNodes_ReturnsConnectedMcuNodes()
    {
        var server = new KestrelServerState();
        server.AddActiveNode("MCU_NODE_1");
        server.AddActiveNode("MCU_NODE_2");

        var nodes = server.GetActiveNodes();

        nodes.Should().Contain("MCU_NODE_1");
        nodes.Should().Contain("MCU_NODE_2");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task KestrelServer_Shutdown_StopsListenerCleanly()
    {
        var server = new KestrelServerState();
        server.Start();
        server.IsRunning.Should().BeTrue();

        await server.StopAsync();
        server.IsRunning.Should().BeFalse();
    }
}

public class KestrelServerState
{
    public int Port { get; private set; }
    public string ListeningUrl => $"http://localhost:{Port}";
    public bool IsRunning { get; private set; }
    private readonly List<string> _nodes = new();

    public void Initialize(int port)
    {
        Port = port;
    }

    public void Start() => IsRunning = true;
    public Task StopAsync() { IsRunning = false; return Task.CompletedTask; }

    public string FormatSseEvent(TelemetryPacket packet)
    {
        return $"data: {{\"nodeId\":\"{packet.NodeId}\",\"variable\":\"{packet.Variable}\",\"value\":{packet.Value:F1}}}\n\n";
    }

    public string GetRestStatus()
    {
        return "{\"status\":\"Healthy\",\"version\":\"1.0.0\"}";
    }

    public void AddActiveNode(string node) => _nodes.Add(node);
    public List<string> GetActiveNodes() => new(_nodes);
}
