using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Host.Outbound;
using TelemetryDashboard.Infrastructure.WebServer;
using TelemetryDashboard.Tests.TestUtilities;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Covers the headless host's outbound path: alerts to Slack and telemetry to an MQTT broker.
/// </summary>
public class OutboundRelayTests
{
    private static ScoredSample Anomalous(string channel = "MCU_A.temp", double z = 4.2) =>
        new(channel, "MCU_A", "temp", 91.4, "C", new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc),
            ZScore: z, IsAnomaly: true, AnalyzerId: "rolling-3sigma", IsSimulated: false);

    private static ScoredSample Unjudged() =>
        new("MCU_A.temp", "MCU_A", "temp", 41.9, "C", new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc),
            ZScore: null, IsAnomaly: null, AnalyzerId: null, IsSimulated: false);

    private sealed class RecordingSlack : ISlackClient
    {
        public List<string> Sent { get; } = new();

        public Task<bool> SendAlertAsync(string webhookUrl, string message)
        {
            lock (Sent) Sent.Add(message);
            return Task.FromResult(true);
        }
    }

    private static async Task<List<string>> DrainAsync(RecordingSlack slack, SlackAlertRelay relay)
    {
        await relay.DisposeAsync();
        lock (slack.Sent) return new List<string>(slack.Sent);
    }

    [Fact]
    public async Task AnAnomalyIsRelayedToTheWebhook()
    {
        var slack = new RecordingSlack();
        var relay = new SlackAlertRelay(slack, "https://hooks.slack.com/services/T0/B0/abcdefghijklmnop");

        relay.OnSampleScored(null, Anomalous());

        List<string> sent = await DrainAsync(slack, relay);
        sent.Should().ContainSingle().Which.Should().Contain("MCU_A.temp").And.Contain("4.20 sigma");
    }

    [Fact]
    public async Task ASampleWithNoVerdictNeverRaisesAnAlert()
    {
        var slack = new RecordingSlack();
        var relay = new SlackAlertRelay(slack, "https://hooks.slack.com/services/T0/B0/abcdefghijklmnop");

        relay.OnSampleScored(null, Unjudged());

        (await DrainAsync(slack, relay)).Should().BeEmpty(
            "a channel the host has not finished learning has not been judged anomalous");
        relay.Considered.Should().Be(0);
    }

    [Fact]
    public async Task APersistentFaultProducesOneMessage_NotOnePerSample()
    {
        var slack = new RecordingSlack();
        var relay = new SlackAlertRelay(slack, "https://hooks.slack.com/services/T0/B0/abcdefghijklmnop",
            cooldown: TimeSpan.FromMinutes(10));

        for (int i = 0; i < 500; i++) relay.OnSampleScored(null, Anomalous());

        (await DrainAsync(slack, relay)).Should().HaveCount(1);
        relay.Considered.Should().Be(500);
        relay.Throttled.Should().Be(499);
    }

    [Fact]
    public void TheNextMessageSaysHowManyWereHeldBack()
    {
        SlackAlertRelay.Compose(Anomalous(), suppressedSinceLast: 499)
            .Should().Contain("499").And.Contain("did not clear");

        SlackAlertRelay.Compose(Anomalous(), suppressedSinceLast: 0)
            .Should().NotContain("held back");
    }

    [Fact]
    public void ASyntheticAnomalyIsLabelledAsSynthetic()
    {
        ScoredSample simulated = Anomalous() with { IsSimulated = true };

        SlackAlertRelay.Compose(simulated, 0).Should().Contain("simulator, not measured");
        SlackAlertRelay.Compose(Anomalous(), 0).Should().NotContain("simulator");
    }

    [Fact]
    public void DifferentChannelsAreThrottledIndependently()
    {
        var throttle = new AlertThrottle(TimeSpan.FromMinutes(5));

        throttle.ShouldSend("a", out _).Should().BeTrue();
        throttle.ShouldSend("b", out _).Should().BeTrue("one noisy channel must not mute another");
        throttle.ShouldSend("a", out _).Should().BeFalse();
    }

    [Fact]
    public void TheCooldownExpiresAndReleasesTheSuppressedCount()
    {
        DateTime now = new(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);
        var throttle = new AlertThrottle(TimeSpan.FromMinutes(5), () => now);

        throttle.ShouldSend("a", out _).Should().BeTrue();
        for (int i = 0; i < 7; i++) throttle.ShouldSend("a", out _).Should().BeFalse();

        now = now.AddMinutes(6);

        throttle.ShouldSend("a", out int suppressed).Should().BeTrue();
        suppressed.Should().Be(7);
    }

    [Fact]
    public void TheMqttPayloadOmitsAVerdictTheHostDidNotReach()
    {
        using JsonDocument unjudged = JsonDocument.Parse(MqttTelemetryRelay.Payload(Unjudged()));
        unjudged.RootElement.TryGetProperty("zscore", out _).Should().BeFalse(
            "a zero z-score would read to a subscriber as a calm channel");

        using JsonDocument judged = JsonDocument.Parse(MqttTelemetryRelay.Payload(Anomalous()));
        judged.RootElement.GetProperty("zscore").GetDouble().Should().Be(4.2);
        judged.RootElement.GetProperty("isAnomaly").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task TopicSeparatorsInAChannelNameCannotForgeExtraTopicLevels()
    {
        await using var relay = new MqttTelemetryRelay(new MqttPublisher(), "plant");
        ScoredSample odd = Anomalous() with { NodeId = "line/3", Variable = "temp#a" };

        relay.TopicFor(odd).Should().Be("plant/line_3/temp_a");
    }

    [Fact]
    public async Task SamplesReachARealBrokerOverTheWire()
    {
        await using var broker = new StubMqttBroker();
        await using var relay = new MqttTelemetryRelay(new MqttPublisher(), "plant");

        (await relay.ConnectAsync("127.0.0.1", broker.Port)).Should().BeTrue();

        relay.OnSampleScored(null, Anomalous());
        relay.OnSampleScored(null, Anomalous(channel: "MCU_A.rpm") with { Variable = "rpm", Value = 1500 });

        (await broker.WaitForAsync(2, TimeSpan.FromSeconds(10))).Should().BeTrue();

        MqttPublication[] received = broker.Received.ToArray();
        received.Select(p => p.Topic).Should().Contain("plant/MCU_A/temp").And.Contain("plant/MCU_A/rpm");

        using JsonDocument decoded = JsonDocument.Parse(received[0].Payload);
        decoded.RootElement.GetProperty("node").GetString().Should().Be("MCU_A");
        decoded.RootElement.GetProperty("value").GetDouble().Should().Be(91.4);
    }

    [Fact]
    public async Task AStalledSenderCausesRefusals_AndTheCountIsReported()
    {
        var queue = new OutboundQueue<int>("test", capacity: 4, (_, token) => Task.Delay(Timeout.Infinite, token));

        for (int i = 0; i < 200; i++) queue.Offer(i);

        queue.Dropped.Should().BeGreaterThan(0, "a bounded queue must refuse rather than grow");
        queue.Summary().Should().Contain("dropped locally");

        await queue.DisposeAsync();
        queue.AbandonedOnShutdown.Should().BeFalse("this sender does honour cancellation");
    }

    [Fact]
    public async Task ASenderThatIgnoresCancellationDoesNotHoldTheHostOpenForever()
    {
        // ISlackClient.SendAlertAsync takes no cancellation token, so this is not hypothetical.
        var queue = new OutboundQueue<int>("stuck", capacity: 2, (_, _) => Task.Delay(Timeout.Infinite));
        queue.Offer(1);

        DateTime started = DateTime.UtcNow;
        await queue.DisposeAsync();

        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(30));
        queue.AbandonedOnShutdown.Should().BeTrue();
        queue.Summary().Should().Contain("did not stop when asked");
    }
}
