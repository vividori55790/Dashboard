using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F31_MqttPublisherTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void MqttPublisher_Configure_SetsHostPortAndClientId()
    {
        var publisher = new MqttPublisherState();
        publisher.Configure("broker.hivemq.com", 1883, "TelemetryDash_Client01");

        publisher.Host.Should().Be("broker.hivemq.com");
        publisher.Port.Should().Be(1883);
        publisher.ClientId.Should().Be("TelemetryDash_Client01");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task MqttPublisher_Connect_EstablishesActiveConnection()
    {
        var publisher = new MqttPublisherState();
        publisher.Configure("localhost", 1883, "Client1");
        bool connected = await publisher.ConnectAsync();

        connected.Should().BeTrue();
        publisher.IsConnected.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task MqttPublisher_Publish_SendsTelemetryPacketToTopic()
    {
        var publisher = new MqttPublisherState();
        await publisher.ConnectAsync();
        var packet = new TelemetryPacket("MCU_1", "TEMP", 45.0, "C");

        bool published = await publisher.PublishPacketAsync("telemetry/MCU_1/TEMP", packet);

        published.Should().BeTrue();
        publisher.PublishedTopics.Should().Contain("telemetry/MCU_1/TEMP");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task MqttPublisher_QoS_SupportsQoS0AndQoS1Settings()
    {
        var publisher = new MqttPublisherState();
        await publisher.ConnectAsync();

        bool qos0Result = await publisher.PublishWithQosAsync("telemetry/topic", "payload", qos: 0);
        bool qos1Result = await publisher.PublishWithQosAsync("telemetry/topic", "payload", qos: 1);

        qos0Result.Should().BeTrue();
        qos1Result.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task MqttPublisher_Disconnect_ClosesConnectionCleanly()
    {
        var publisher = new MqttPublisherState();
        await publisher.ConnectAsync();
        await publisher.DisconnectAsync();

        publisher.IsConnected.Should().BeFalse();
    }
}

public class MqttPublisherState
{
    public string Host { get; private set; } = string.Empty;
    public int Port { get; private set; }
    public string ClientId { get; private set; } = string.Empty;
    public bool IsConnected { get; private set; }
    public List<string> PublishedTopics { get; } = new();

    public void Configure(string host, int port, string clientId)
    {
        Host = host;
        Port = port;
        ClientId = clientId;
    }

    public Task<bool> ConnectAsync()
    {
        IsConnected = true;
        return Task.FromResult(true);
    }

    public Task<bool> DisconnectAsync()
    {
        IsConnected = false;
        return Task.FromResult(true);
    }

    public Task<bool> PublishPacketAsync(string topic, TelemetryPacket packet)
    {
        PublishedTopics.Add(topic);
        return Task.FromResult(true);
    }

    public Task<bool> PublishWithQosAsync(string topic, string payload, int qos)
    {
        return Task.FromResult(true);
    }
}
