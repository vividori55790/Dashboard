using System.Diagnostics;
using System.Net;
using System.Net.Http;
using TelemetryDashboard.Core.Analytics.Detectors;
using TelemetryDashboard.Infrastructure.Analytics;

namespace TelemetryDashboard.Tests;

/// <summary>
/// What the model client does when the model answers, and — the part that matters — what it does
/// when it does not.
/// </summary>
/// <remarks>
/// The transport is injected rather than reached over a real socket. That is not a shortcut: a
/// server that is genuinely slow, genuinely refusing and genuinely returning malformed JSON is hard
/// to arrange reliably and impossible to arrange quickly, and these are the paths that must be
/// covered rather than assumed. The timeout is still enforced by the real
/// <see cref="CancellationTokenSource"/> in the endpoint, so the deadline under test is the shipped
/// one.
/// </remarks>
public class InferenceEndpointTests
{
    private static readonly Uri Endpoint = new("http://127.0.0.1:9/score");

    /// <summary>A transport that answers however the test tells it to.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public ScriptedHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
            _respond = respond;

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return await _respond(request, cancellationToken);
        }
    }

    private static ScriptedHandler Answering(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }));

    private static ScriptedHandler Silent(TimeSpan delay) =>
        new(async (_, token) =>
        {
            await Task.Delay(delay, token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"score\":0.1}") };
        });

    private static ScriptedHandler Refusing() =>
        new((_, _) => throw new HttpRequestException("connection refused"));

    private static InferenceRequest Window(string channel = "NODE_1.TEMP") =>
        new(channel, new[] { 1.0, 2.0, 3.0 }, DetectorSignals.Origin, "test-model");

    // ---------------------------------------------------------------
    // The endpoint itself
    // ---------------------------------------------------------------

    [Fact]
    public async Task AWellFormedAnswerIsReadAndTheWindowItScoredIsSentIntact()
    {
        ScriptedHandler handler = Answering("""{ "score": 0.93, "isAnomaly": true, "modelId": "v3" }""");
        using var endpoint = new HttpInferenceEndpoint(Endpoint, TimeSpan.FromSeconds(2), "test-model", handler);

        InferenceScore? score = await endpoint.ScoreAsync(Window(), CancellationToken.None);

        score.Should().NotBeNull();
        score!.Score.Should().Be(0.93);
        score.ModelJudgement.Should().BeTrue();
        score.ModelId.Should().Be("v3");
        endpoint.Tally.Accepted.Should().Be(1);

        handler.LastBody.Should().Contain("NODE_1.TEMP").And.Contain("samples").And.Contain("test-model");
    }

    [Fact]
    public async Task AnEndpointThatIsTooSlowProducesNoScoreAndIsCountedAsATimeout()
    {
        using var endpoint = new HttpInferenceEndpoint(
            Endpoint, TimeSpan.FromMilliseconds(80), "test-model", Silent(TimeSpan.FromSeconds(30)));

        var elapsed = Stopwatch.StartNew();
        InferenceScore? score = await endpoint.ScoreAsync(Window(), CancellationToken.None);
        elapsed.Stop();

        score.Should().BeNull("a model that did not answer has not judged anything");
        endpoint.Tally.TimedOut.Should().Be(1);
        endpoint.Tally.Accepted.Should().Be(0);
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "the configured deadline is the one in force");
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{ "verdict": "everything is fine" }""")]
    [InlineData("""{ "score": "0.9" }""")]
    [InlineData("""{ "score": null }""")]
    [InlineData("[0.9]")]
    public async Task AnAnswerWithNoReadableScoreYieldsNothing_NotAZero(string body)
    {
        ScriptedHandler handler = Answering(body);
        using var endpoint = new HttpInferenceEndpoint(Endpoint, TimeSpan.FromSeconds(2), "test-model", handler);

        (await endpoint.ScoreAsync(Window(), CancellationToken.None)).Should().BeNull();

        endpoint.Tally.Unusable.Should().Be(1);
        endpoint.Tally.Accepted.Should().Be(0);
    }

    [Fact]
    public async Task AServerErrorAndARefusedConnectionAreBothCountedAsNoAnswer()
    {
        using var failing = new HttpInferenceEndpoint(
            Endpoint, TimeSpan.FromSeconds(2), "test-model", Answering("boom", HttpStatusCode.InternalServerError));
        using var refused = new HttpInferenceEndpoint(
            Endpoint, TimeSpan.FromSeconds(2), "test-model", Refusing());

        (await failing.ScoreAsync(Window(), CancellationToken.None)).Should().BeNull();
        (await refused.ScoreAsync(Window(), CancellationToken.None)).Should().BeNull();

        failing.Tally.Refused.Should().Be(1);
        refused.Tally.Refused.Should().Be(1);
    }

    [Fact]
    public async Task ShutdownIsRethrownRatherThanMisreportedAsASlowModel()
    {
        using var endpoint = new HttpInferenceEndpoint(
            Endpoint, TimeSpan.FromSeconds(30), "test-model", Silent(TimeSpan.FromSeconds(30)));
        using var shutdown = new CancellationTokenSource();

        Task<InferenceScore?> scoring = endpoint.ScoreAsync(Window(), shutdown.Token);
        shutdown.Cancel();

        await FluentActions.Awaiting(() => scoring).Should().ThrowAsync<OperationCanceledException>();
        endpoint.Tally.TimedOut.Should().Be(0, "a clean stop is not a model failure");
    }

    // ---------------------------------------------------------------
    // The bounded hand-off
    // ---------------------------------------------------------------

    [Fact]
    public async Task WindowsOfferedFasterThanTheModelCanScoreAreRefusedAndCounted()
    {
        using var release = new SemaphoreSlim(0);
        var tally = new InferenceTally();
        var endpoint = new HttpInferenceEndpoint(
            Endpoint, TimeSpan.FromSeconds(30), "test-model",
            new ScriptedHandler(async (_, token) =>
            {
                await release.WaitAsync(token);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"score\":0.1}") };
            }));

        await using (var queue = new InferenceDispatchQueue(endpoint, capacity: 2, tally, (_, _) => { }))
        {
            for (int i = 0; i < 50; i++) queue.Offer(Window("NODE_" + i + ".TEMP"));

            tally.Offered.Should().Be(50);
            tally.Dropped.Should().BeGreaterThan(0,
                "a bounded queue must refuse and count rather than grow while the model is stuck");
            release.Release(50);
        }

        endpoint.Dispose();
    }

    // ---------------------------------------------------------------
    // The detector on the ingest path
    // ---------------------------------------------------------------

    private sealed class ShiftableClock
    {
        public TimeSpan Offset;
        public DateTime Now() => DateTime.UtcNow + Offset;
    }

    private static InferenceSpec Spec(int window = 8, double threshold = 0.8, int maxScoreAgeMs = 5_000) => new()
    {
        Runtime = InferenceRuntime.Http,
        Endpoint = Endpoint.ToString(),
        ModelId = "test-model",
        Window = window,
        Threshold = threshold,
        MaxScoreAgeMs = maxScoreAgeMs,
        SamplesBetweenRequests = 4
    };

    private static bool WaitFor(Func<bool> condition) =>
        SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(10));

    /// <summary>
    /// Feeds the detector until it has a verdict to give, or gives up.
    /// </summary>
    /// <remarks>
    /// Waiting on the tally instead would race: the endpoint counts a score the moment it parses
    /// one, which is before the dispatch pump has handed it to the detector. Asking the detector is
    /// the only thing that actually answers "has the model's opinion arrived".
    /// </remarks>
    private static DetectorVerdict WaitForVerdict(RemoteInferenceDetector detector, string channel)
    {
        DetectorVerdict latest = DetectorVerdict.NotJudged("never asked");
        WaitFor(() => (latest = detector.Evaluate(channel, 10.0, DetectorSignals.Origin)).HasVerdict);
        return latest;
    }

    [Fact]
    public async Task TheDetectorWithholdsAVerdictUntilTheModelHasActuallyScoredSomething()
    {
        using var endpoint = new HttpInferenceEndpoint(
            Endpoint, TimeSpan.FromSeconds(2), "test-model", Answering("""{ "score": 0.93 }"""));
        await using var detector = new RemoteInferenceDetector(endpoint, Spec());

        IReadOnlyList<DetectorVerdict> collecting =
            DetectorSignals.Run(detector, "NODE_1.TEMP", DetectorSignals.Wobble(7, 10.0, 0.1));

        collecting.Should().AllSatisfy(v => v.HasVerdict.Should().BeFalse());
        collecting[^1].Reason.Should().Contain("collecting the model's window");

        detector.Evaluate("NODE_1.TEMP", 10.0, DetectorSignals.Origin);  // window completes, request goes out
        DetectorVerdict judged = WaitForVerdict(detector, "NODE_1.TEMP");

        detector.Tally.Accepted.Should().BeGreaterThan(0, "the model answered");
        judged.HasVerdict.Should().BeTrue();
        judged.Score.Should().Be(0.93);
        judged.ScoreKind.Should().Be(DetectorScoreKind.ModelScore);
        judged.IsAnomaly.Should().BeTrue("0.93 is over the configured 0.8");
        judged.DetectorId.Should().Contain("test-model");
        judged.Reason.Should().Contain("host threshold");
    }

    [Fact]
    public async Task AModelThatStatesItsOwnVerdictIsObeyedRatherThanReThresholded()
    {
        using var endpoint = new HttpInferenceEndpoint(
            Endpoint, TimeSpan.FromSeconds(2), "test-model",
            Answering("""{ "score": 0.2, "isAnomaly": true }"""));
        await using var detector = new RemoteInferenceDetector(endpoint, Spec());

        DetectorSignals.Run(detector, "NODE_1.TEMP", DetectorSignals.Wobble(8, 10.0, 0.1));
        DetectorVerdict judged = WaitForVerdict(detector, "NODE_1.TEMP");

        judged.IsAnomaly.Should().BeTrue("the model said so, and 0.2 would not have crossed the host threshold");
        judged.Reason.Should().Contain("the model's own verdict");
    }

    [Fact]
    public async Task AModelThatIsDownProducesNoVerdictAtAll_ForAsLongAsItStaysDown()
    {
        using var endpoint = new HttpInferenceEndpoint(Endpoint, TimeSpan.FromSeconds(2), "test-model", Refusing());
        await using var detector = new RemoteInferenceDetector(endpoint, Spec());

        IReadOnlyList<DetectorVerdict> verdicts =
            DetectorSignals.Run(detector, "NODE_1.TEMP", DetectorSignals.Wobble(60, 10.0, 0.1));

        WaitFor(() => detector.Tally.Offered > 0).Should().BeTrue();

        verdicts.Should().AllSatisfy(v => v.HasVerdict.Should().BeFalse());
        verdicts.Should().AllSatisfy(v => v.IsAnomaly.Should().BeFalse());
        verdicts[^1].Reason.Should().Contain("has not returned a usable score");
        detector.Tally.Accepted.Should().Be(0);
    }

    [Fact]
    public async Task AModelThatIsSlowNeverBlocksTheIngestPath()
    {
        using var endpoint = new HttpInferenceEndpoint(
            Endpoint, TimeSpan.FromMilliseconds(100), "test-model", Silent(TimeSpan.FromSeconds(30)));
        await using var detector = new RemoteInferenceDetector(endpoint, Spec());

        var elapsed = Stopwatch.StartNew();
        IReadOnlyList<DetectorVerdict> verdicts =
            DetectorSignals.Run(detector, "NODE_1.TEMP", DetectorSignals.Wobble(40, 10.0, 0.1));
        elapsed.Stop();

        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "forty samples must not wait on a model that answers in thirty seconds");
        verdicts.Should().AllSatisfy(v => v.HasVerdict.Should().BeFalse());
        WaitFor(() => detector.Tally.TimedOut > 0).Should().BeTrue("the request was abandoned at its deadline");
    }

    [Fact]
    public async Task AScoreThatHasAgedOutStopsBeingQuoted()
    {
        var clock = new ShiftableClock();
        using var endpoint = new HttpInferenceEndpoint(
            Endpoint, TimeSpan.FromSeconds(2), "test-model", Answering("""{ "score": 0.93 }"""));
        await using var detector = new RemoteInferenceDetector(
            endpoint, Spec(maxScoreAgeMs: 1_000), label: null, clock: clock.Now);

        DetectorSignals.Run(detector, "NODE_1.TEMP", DetectorSignals.Wobble(8, 10.0, 0.1));
        WaitForVerdict(detector, "NODE_1.TEMP").HasVerdict.Should().BeTrue("the score is fresh");

        clock.Offset = TimeSpan.FromSeconds(30);
        DetectorVerdict aged = detector.Evaluate("NODE_1.TEMP", 10.0, DetectorSignals.Origin);

        aged.HasVerdict.Should().BeFalse("a model that has stopped answering must stop producing judgements");
        aged.IsAnomaly.Should().BeFalse();
        aged.Reason.Should().Contain("past the 1000 ms limit");
        detector.Tally.Stale.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task TheTallyDistinguishesEveryWayTheModelCanFail()
    {
        using var endpoint = new HttpInferenceEndpoint(
            Endpoint, TimeSpan.FromSeconds(2), "test-model", Answering("""{ "verdict": "fine" }"""));
        await using var detector = new RemoteInferenceDetector(endpoint, Spec());

        DetectorSignals.Run(detector, "NODE_1.TEMP", DetectorSignals.Wobble(20, 10.0, 0.1));
        WaitFor(() => endpoint.Tally.Unusable > 0).Should().BeTrue();

        endpoint.Tally.Summary("test").Should().Contain("unreadable");
        detector.Tally.Summary(detector.DetectorId).Should().Contain("offered");
    }
}
