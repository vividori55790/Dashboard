using System.Text.Json;
using TelemetryDashboard.Core.Query;
using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Subscription behaviour: a client is sent the channels it asked for, at the rate it asked for,
/// reduced to the number of points it can draw.
/// </summary>
public class SeriesQuerySubscriptionTests
{
    /// <summary>A subscriber that keeps every frame handed to it, for inspection.</summary>
    private sealed class RecordingSubscriber : ITelemetrySubscriber
    {
        public RecordingSubscriber(string id) => Id = id;

        public string Id { get; }
        public string Transport => "test";
        public bool IsConnected { get; set; } = true;
        public List<byte[]> Frames { get; } = new();

        public Task SendAsync(ReadOnlyMemory<byte> utf8Payload, CancellationToken cancellationToken)
        {
            Frames.Add(utf8Payload.ToArray());
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public JsonElement LastFrame() => JsonDocument.Parse(Frames[^1]).RootElement;
    }

    /// <summary>The instant every test pumps at, so windows are deterministic.</summary>
    private const double NowSec = 2_000.0;

    /// <summary>Two channels sampled at 1 kHz, the newest sample landing exactly on <see cref="NowSec"/>.</summary>
    private static SeriesStore StoreWithTwoChannels(int samplesEach)
    {
        var store = new SeriesStore(samplesPerChannel: samplesEach);
        for (int i = 0; i < samplesEach; i++)
        {
            double t = NowSec - ((samplesEach - 1 - i) * 0.001);
            store.Append("A.temp", Math.Sin(i * 0.01) * 5.0 + 20.0, t);
            store.Append("B.pressure", i % 17, t);
        }
        return store;
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public async Task Subscriber_ReceivesOnlyTheChannelsItAskedFor()
    {
        var hub = new TelemetryBroadcastHub();
        var subscriber = new RecordingSubscriber("s1");
        hub.TryAdd(subscriber);
        hub.Subscribe("s1", new SubscriptionOptions(new[] { "A.temp" }, maxUpdateHz: 10, maxPoints: 500), 0.0);

        var pump = new SeriesBroadcastPump(hub, new SeriesQueryService(StoreWithTwoChannels(50_000)));
        await pump.PumpOnceAsync(NowSec);

        JsonElement series = subscriber.LastFrame().GetProperty("series");
        series.GetArrayLength().Should().Be(1);
        series[0].GetProperty("channel").GetString().Should().Be("A.temp");
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public async Task Subscriber_IsHeldToTheUpdateRateItAskedFor()
    {
        var hub = new TelemetryBroadcastHub();
        var subscriber = new RecordingSubscriber("s1");
        hub.TryAdd(subscriber);
        hub.Subscribe("s1", new SubscriptionOptions(new[] { "A.temp" }, maxUpdateHz: 10), NowSec);

        var pump = new SeriesBroadcastPump(hub, new SeriesQueryService(StoreWithTwoChannels(10_000)));

        // A hundred passes across a tenth of a second of wall time. At 10 Hz that is one frame.
        for (int tick = 0; tick < 100; tick++) await pump.PumpOnceAsync(NowSec + (tick * 0.001));

        subscriber.Frames.Should().HaveCount(1);

        await pump.PumpOnceAsync(NowSec + 0.2);
        subscriber.Frames.Should().HaveCount(2, "a tenth of a second later a second frame is due");
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public async Task Subscriber_IsNeverSentMorePointsThanItCanDraw()
    {
        var hub = new TelemetryBroadcastHub();
        var subscriber = new RecordingSubscriber("s1");
        hub.TryAdd(subscriber);
        hub.Subscribe("s1", new SubscriptionOptions(new[] { "A.temp", "B.pressure" }, maxPoints: 800), 0.0);

        var pump = new SeriesBroadcastPump(hub, new SeriesQueryService(StoreWithTwoChannels(200_000)));
        await pump.PumpOnceAsync(NowSec);

        JsonElement frame = subscriber.LastFrame();
        foreach (JsonElement series in frame.GetProperty("series").EnumerateArray())
        {
            series.GetProperty("points").GetArrayLength().Should().BeLessThanOrEqualTo(800);
        }
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public async Task Frame_StatesThatItIsAReductionAndHowCoarse()
    {
        var hub = new TelemetryBroadcastHub();
        var subscriber = new RecordingSubscriber("s1");
        hub.TryAdd(subscriber);
        hub.Subscribe("s1", new SubscriptionOptions(new[] { "A.temp" }, maxPoints: 500, windowSec: 3_600), 0.0);

        var pump = new SeriesBroadcastPump(hub, new SeriesQueryService(StoreWithTwoChannels(100_000)));
        await pump.PumpOnceAsync(NowSec);

        JsonElement frame = subscriber.LastFrame();
        frame.GetProperty("isReduced").GetBoolean().Should().BeTrue();
        frame.GetProperty("sourceSampleCount").GetInt64().Should().Be(100_000);

        JsonElement reduction = frame.GetProperty("series")[0].GetProperty("reduction");
        reduction.GetProperty("method").GetString().Should().Be("minmax");
        reduction.GetProperty("preservesExtremes").GetBoolean().Should().BeTrue();
        reduction.GetProperty("bucketWidthSec").GetDouble().Should().BeGreaterThan(0.0);
        reduction.GetProperty("discardedSampleCount").GetInt32().Should().BeGreaterThan(0);
        reduction.GetProperty("discarded").GetString().Should().NotBeNullOrWhiteSpace();
        reduction.GetProperty("sourceMinimum").GetDouble().Should().BeLessThan(
            reduction.GetProperty("sourceMaximum").GetDouble());
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public async Task Frame_ReportsRawPointsAsRawRatherThanAsAReduction()
    {
        var hub = new TelemetryBroadcastHub();
        var subscriber = new RecordingSubscriber("s1");
        hub.TryAdd(subscriber);
        hub.Subscribe("s1", new SubscriptionOptions(new[] { "A.temp" }, maxPoints: 2_000), 0.0);

        var pump = new SeriesBroadcastPump(hub, new SeriesQueryService(StoreWithTwoChannels(100)));
        await pump.PumpOnceAsync(NowSec);

        JsonElement frame = subscriber.LastFrame();
        frame.GetProperty("isReduced").GetBoolean().Should().BeFalse(
            "a hundred samples inside a two-thousand-point budget were not reduced at all");
        frame.GetProperty("series")[0].GetProperty("reduction").GetProperty("method")
             .GetString().Should().Be("none");
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public async Task RawFanOut_SkipsSubscribedClientsAndStillServesEveryoneElse()
    {
        var hub = new TelemetryBroadcastHub();
        var subscribed = new RecordingSubscriber("s1");
        var plain = new RecordingSubscriber("s2");
        hub.TryAdd(subscribed);
        hub.TryAdd(plain);
        hub.Subscribe("s1", new SubscriptionOptions(new[] { "A.temp" }, maxUpdateHz: 1), 0.0);

        for (int i = 0; i < 25; i++) await hub.BroadcastAsync(new byte[] { 1, 2, 3 });

        plain.Frames.Should().HaveCount(25, "a client that asked for nothing keeps the unfiltered feed");
        subscribed.Frames.Should().BeEmpty(
            "it asked for one channel at 1 Hz; handing it the ingest rate as well defeats the request");
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public void Parser_ReadsASubscription()
    {
        SubscriptionCommandKind kind = SubscriptionRequestParser.Parse(
            """{"type":"subscribe","channels":["A.temp","B.pressure"],"maxUpdateHz":4,"maxPoints":900,"windowSec":30,"reduction":"lttb"}""",
            out SubscriptionOptions? options);

        kind.Should().Be(SubscriptionCommandKind.Subscribe);
        options!.Channels.Should().Equal("A.temp", "B.pressure");
        options.MaxUpdateHz.Should().Be(4);
        options.MaxPoints.Should().Be(900);
        options.WindowSec.Should().Be(30);
        options.Method.Should().Be(ReductionMethod.LargestTriangleThreeBuckets);
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public void Parser_LeavesApplicationCommandsAlone()
    {
        SubscriptionRequestParser.Parse("""{"type":"setRelay","channel":"K1","state":true}""", out _)
            .Should().Be(SubscriptionCommandKind.NotACommand);
        SubscriptionRequestParser.Parse("RESET", out _).Should().Be(SubscriptionCommandKind.NotACommand);
        SubscriptionRequestParser.Parse("{not json", out _).Should().Be(SubscriptionCommandKind.NotACommand);
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public void Options_ClampAnUnservableRequestInsteadOfHonouringIt()
    {
        var options = new SubscriptionOptions(
            new[] { "A.temp" }, maxUpdateHz: 100_000, maxPoints: 50_000_000, windowSec: -1);

        options.MaxUpdateHz.Should().Be(SubscriptionOptions.MaxSupportedUpdateHz);
        options.MaxPoints.Should().Be(SubscriptionOptions.MaxSupportedPoints);
        options.WindowSec.Should().Be(SubscriptionOptions.DefaultWindowSec);
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public async Task Unsubscribe_ReturnsAClientToTheUnfilteredFeed()
    {
        var hub = new TelemetryBroadcastHub();
        var subscriber = new RecordingSubscriber("s1");
        hub.TryAdd(subscriber);
        hub.Subscribe("s1", new SubscriptionOptions(new[] { "A.temp" }), 0.0);

        await hub.BroadcastAsync(new byte[] { 9 });
        subscriber.Frames.Should().BeEmpty();

        hub.Unsubscribe("s1").Should().BeTrue();
        await hub.BroadcastAsync(new byte[] { 9 });
        subscriber.Frames.Should().HaveCount(1);
    }
}
