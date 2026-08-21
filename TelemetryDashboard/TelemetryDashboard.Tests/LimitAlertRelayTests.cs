using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Host.Outbound;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Getting an engineering-limit event off the machine.
/// </summary>
/// <remarks>
/// The relay forwarded only what the rolling detector flagged, and that detector does not find a
/// steady value unusual — measured at |z| never above 1.94 across 107 samples held 42–119 V past a
/// hard limit. So a converter sitting outside its safe band told nobody, and an unattended host
/// that notices a fault and stays quiet has failed at the one job it was left alone to do.
/// </remarks>
public class LimitAlertRelayTests
{
    private const string Webhook = "https://hooks.slack.com/services/T0/B0/abcdefghijklmnop";

    private sealed class RecordingSlack : ISlackClient
    {
        public List<string> Sent { get; } = new();

        public Task<bool> SendAlertAsync(string webhookUrl, string message)
        {
            lock (Sent) Sent.Add(message);
            return Task.FromResult(true);
        }
    }

    private static readonly ChannelLimit Ceiling = ChannelLimit.Parse("grid.voltage[V] < 300");

    private static ScoredSample Sample(
        double value, LimitTransition transition, double? zScore = null, bool? isAnomaly = null) => new(
        Channel: "SIM:COM3.grid.voltage",
        NodeId: "SIM:COM3",
        Variable: "grid.voltage",
        Value: value,
        Unit: "V",
        TimestampUtc: new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc),
        ZScore: zScore,
        IsAnomaly: isAnomaly,
        AnalyzerId: zScore is null ? null : "zscore-rolling",
        IsSimulated: true,
        BreachedLimits: new[] { new BreachedLimit(Ceiling, transition) });

    private static async Task<List<string>> DrainAsync(RecordingSlack slack, SlackAlertRelay relay)
    {
        await relay.DisposeAsync();
        lock (slack.Sent) return new List<string>(slack.Sent);
    }

    [Fact]
    public async Task ACrossingIsAlertedEvenWhenTheDetectorHasNoOpinion()
    {
        // The whole point. A limit does not need a baseline, and this sample has no verdict at all.
        var slack = new RecordingSlack();
        var relay = new SlackAlertRelay(slack, Webhook);

        relay.OnSampleScored(null, Sample(384, LimitTransition.Entered));

        List<string> sent = await DrainAsync(slack, relay);
        sent.Should().ContainSingle();
        sent[0].Should().Contain("Outside limit")
            .And.Contain("384 is above the 300 ceiling")
            .And.Contain("grid.voltage[V] < 300")
            .And.Contain("A limit does not need one");
    }

    [Fact]
    public async Task ASustainedBreachSendsNothingFurther()
    {
        // /api/limits carries how long it has lasted. A message per sample is how an alert
        // channel gets muted.
        var slack = new RecordingSlack();
        var relay = new SlackAlertRelay(slack, Webhook);

        relay.OnSampleScored(null, Sample(384, LimitTransition.Entered));
        for (int i = 0; i < 40; i++) relay.OnSampleScored(null, Sample(390 + i, LimitTransition.Sustained));

        (await DrainAsync(slack, relay)).Should().ContainSingle();
    }

    [Fact]
    public async Task TheRecoveryIsSentEvenThoughTheCrossingJustUsedTheQuietPeriod()
    {
        // The defect a live run found: both shared one throttle key, so the host logged four
        // crossings and four recoveries and the webhook received one message. An alert channel
        // that says "it broke" and never "it is fine" leaves an operator believing a machine is
        // still out of band hours after it recovered.
        var slack = new RecordingSlack();
        var relay = new SlackAlertRelay(slack, Webhook, cooldown: TimeSpan.FromMinutes(30));

        relay.OnSampleScored(null, Sample(384, LimitTransition.Entered));
        relay.OnSampleScored(null, Sample(280, LimitTransition.Cleared));

        List<string> sent = await DrainAsync(slack, relay);
        sent.Should().HaveCount(2);
        sent[0].Should().Contain("Outside limit");
        sent[1].Should().Contain("Limit cleared").And.Contain("back inside");
    }

    [Fact]
    public async Task ARecoveryForABreachNobodyWasToldAboutIsNotSent()
    {
        // Otherwise "back inside" is the first this reader hears of either, which reads as an
        // alert about nothing.
        var slack = new RecordingSlack();
        var relay = new SlackAlertRelay(slack, Webhook);

        relay.OnSampleScored(null, Sample(280, LimitTransition.Cleared));

        (await DrainAsync(slack, relay)).Should().BeEmpty();
    }

    [Fact]
    public async Task FlappingCostsOnePairPerQuietPeriodAndLeavesNoRecoveryOrphaned()
    {
        var slack = new RecordingSlack();
        var relay = new SlackAlertRelay(slack, Webhook, cooldown: TimeSpan.FromMinutes(30));

        for (int i = 0; i < 5; i++)
        {
            relay.OnSampleScored(null, Sample(384, LimitTransition.Entered));
            relay.OnSampleScored(null, Sample(280, LimitTransition.Cleared));
        }

        List<string> sent = await DrainAsync(slack, relay);
        sent.Should().HaveCount(2, "the quiet period bounds the crossings, and a recovery is only "
            + "sent for a crossing that was");
        sent.Count(m => m.Contains("Limit cleared")).Should().Be(1);
    }

    [Fact]
    public async Task ALimitAndAnAnomalyOnOneChannelBothGetThrough()
    {
        // Separate throttle keys. Sharing the channel's would let an ordinary anomaly's quiet
        // period swallow the message that says a machine is outside what it may safely do.
        var slack = new RecordingSlack();
        var relay = new SlackAlertRelay(slack, Webhook, cooldown: TimeSpan.FromMinutes(30));

        relay.OnSampleScored(null, Sample(384, LimitTransition.Entered, zScore: 4.2, isAnomaly: true));

        List<string> sent = await DrainAsync(slack, relay);
        sent.Should().HaveCount(2);
        sent.Should().Contain(m => m.Contains("Outside limit"));
        sent.Should().Contain(m => m.Contains("*Anomaly*"));
    }

    [Fact]
    public void AnAnomalyMessageMentionsTheLimitAsContextAndNotAsItsHeadline()
    {
        // Appended inside the description it landed between the reading and the timestamp —
        // "2.62 sigma) OUTSIDE LIMIT: grid.voltage[V] < 300 at 2026-08-21" — which reads as
        // though the limit had a time on it.
        string message = SlackAlertRelay.Compose(
            Sample(384, LimitTransition.Sustained, zScore: 4.2, isAnomaly: true), suppressedSinceLast: 0);

        message.Should().MatchRegex(@"\*Anomaly\*.*4\.20 sigma.*at 2026-03-01 12:00:00 UTC");
        message.Should().Contain("raised its own alert");
    }

    // ---- MQTT --------------------------------------------------------------

    [Fact]
    public void TheMqttPayloadCarriesTheLimitStateSeparatelyFromTheVerdict()
    {
        using JsonDocument doc = JsonDocument.Parse(
            MqttTelemetryRelay.Payload(Sample(384, LimitTransition.Entered)));

        doc.RootElement.GetProperty("outsideLimit").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("limits").EnumerateArray()
            .Select(e => e.GetString()).Should().Equal("grid.voltage[V] < 300");
        doc.RootElement.TryGetProperty("isAnomaly", out _).Should().BeFalse(
            "an absent verdict stays absent; a subscriber reading false would see a calm channel");
    }

    [Fact]
    public void AReadingInsideItsBandCarriesNoLimitFields()
    {
        // Absent rather than false, so a subscriber sees the fields only when they mean something.
        using JsonDocument doc = JsonDocument.Parse(
            MqttTelemetryRelay.Payload(Sample(280, LimitTransition.Cleared)));

        doc.RootElement.TryGetProperty("outsideLimit", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("limits", out _).Should().BeFalse();
    }

    [Fact]
    public void ARecoveryIsNotABreach()
    {
        // The list carries both, so "non-empty" is not the same question as "outside".
        Sample(280, LimitTransition.Cleared).BreachesALimit.Should().BeFalse();
        Sample(384, LimitTransition.Entered).BreachesALimit.Should().BeTrue();
        Sample(384, LimitTransition.Sustained).BreachesALimit.Should().BeTrue();
    }
}
