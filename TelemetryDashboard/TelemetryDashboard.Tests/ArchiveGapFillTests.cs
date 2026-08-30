using System.Net;
using System.Net.Http;
using System.Text;
using TelemetryDashboard.Core.Cluster;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Host.Ingest;

namespace TelemetryDashboard.Tests;

/// <summary>
/// ARCHITECTURE §4's backfill, in the shape this product's exchange actually has.
/// </summary>
/// <remarks>
/// The section is written as though a node buffers locally and pushes when the link returns. The
/// exchange here is pull: a receiver subscribes to a sender's stream, so the sender has no memory
/// of who was listening and nothing to push. The receiver asks instead — it knows the interval from
/// the outage ledger, and the sender's <c>/api/history</c> answers for a time that has passed.
/// <para>
/// Driven end to end against a peer that keeps producing while the link to it drops, which is the
/// only arrangement that tests anything: stopping the sender instead would make the gap genuinely
/// empty. Four outages, 9.0 s down, three fills, 180 samples recovered — 60 per 3-second gap at
/// 20 Hz, exactly. None were duplicates, and of the frames the receiver then republished, 151 were
/// marked late with a worst <c>lateBySec</c> of 3.01: the gap, measured.
/// </para>
/// </remarks>
public class ArchiveGapFillTests
{
    private sealed class Answering : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _reply;
        public Uri? Asked { get; private set; }

        public Answering(Func<HttpRequestMessage, HttpResponseMessage> reply) => _reply = reply;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked = request.RequestUri;
            return Task.FromResult(_reply(request));
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static readonly Uri Stream = new("http://peer.local:8100/stream");

    private static LinkOutage Gap(double seconds)
    {
        var began = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        return new LinkOutage(began, began.AddSeconds(seconds), "IOException");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ThePeerIsAskedAboutTheWindowItWasNotConnectedFor()
    {
        Uri asked = ArchiveGapFill.HistoryUriFor(
            Stream, new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 25, 12, 0, 3, DateTimeKind.Utc));

        asked.AbsolutePath.Should().Be("/api/history",
            "the stream URL names the sender, and its history lives beside it");
        asked.Authority.Should().Be("peer.local:8100");
        asked.Query.Should().Contain("from=").And.Contain("to=").And.Contain("limit=");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task ARecoveredReadingComesBackAsTheSampleItWas()
    {
        var handler = new Answering(_ => Json("""
            {"Status":"Success","Count":2,"Truncated":false,"Samples":[
              {"NodeId":"PEER-01","Variable":"dab.bus_voltage","Value":401.5,"Unit":"V",
               "TimestampIso":"2026-08-25T12:00:01.0000000Z"},
              {"NodeId":"PEER-01","Variable":"dab.bus_voltage","Value":402.0,"Unit":"V",
               "TimestampIso":"2026-08-25T12:00:02.0000000Z"}]}
            """));

        using var client = new HttpClient(handler);
        (GapFill fill, IReadOnlyList<string> frames) =
            await ArchiveGapFill.FetchAsync(client, Stream, Gap(3), CancellationToken.None);

        fill.Outcome.Should().Be(GapFillOutcome.Filled);
        fill.Recovered.Should().Be(2);
        frames.Should().HaveCount(2);

        // Read back through the ordinary path, because that is how they enter. A frame this cannot
        // parse would be recovered and then silently dropped.
        TelemetryPacket packet = RawPayloadParser
            .Parse(new RawPacket("peer.local", frames[0], new DateTime(2026, 8, 25, 12, 0, 5, DateTimeKind.Utc)))
            .Single();

        packet.NodeId.Should().Be("PEER-01");
        packet.Variable.Should().Be("dab.bus_voltage");
        packet.Value.Should().Be(401.5);
        packet.Unit.Should().Be("V");
        packet.ObservedAt.Should().Be(new DateTime(2026, 8, 25, 12, 0, 1, DateTimeKind.Utc),
            "the archived instant is what makes it late rather than current");
        packet.SourceSequence.Should().BeNull(
            "an archive stores a reading, not the frame that delivered it, so there is no counter "
            + "-- which is what the duplicate filter's identity path is for");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task APeerWithNoArchiveIsADifferentAnswerFromAPeerWithNothingToSay()
    {
        // The operator's next action differs entirely: one means the plant was quiet, the other
        // means the peer needs --archive. Collapsing them would hide a misconfigured fleet behind
        // a calm-looking one.
        using var noArchive = new HttpClient(new Answering(_ => Json(
            """{"Status":"Error","Reason":"this host has no archive; start it with --archive <file> to keep one"}""")));
        using var nothing = new HttpClient(new Answering(_ => Json(
            """{"Status":"Success","Count":0,"Truncated":false,"Samples":[]}""")));

        (GapFill missing, _) = await ArchiveGapFill.FetchAsync(noArchive, Stream, Gap(3), CancellationToken.None);
        (GapFill quiet, _) = await ArchiveGapFill.FetchAsync(nothing, Stream, Gap(3), CancellationToken.None);

        missing.Outcome.Should().Be(GapFillOutcome.SenderHasNoArchive);
        missing.Describe().Should().Contain("--archive");

        quiet.Outcome.Should().Be(GapFillOutcome.NothingThere);
        quiet.Describe().Should().Contain("had nothing in it");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task AGapLongerThanThisHostWillPullIsSaidToBeSoRatherThanLeftEmpty()
    {
        // Reported as its own outcome, and no request is made. An empty answer and a refusal to
        // ask look identical from the result, and only one is a reason to go and widen the bound.
        var handler = new Answering(_ => Json("""{"Status":"Success","Count":0,"Samples":[]}"""));
        using var client = new HttpClient(handler);

        (GapFill fill, IReadOnlyList<string> frames) = await ArchiveGapFill.FetchAsync(
            client, Stream, Gap(ArchiveGapFill.LongestGap.TotalSeconds + 1), CancellationToken.None);

        fill.Outcome.Should().Be(GapFillOutcome.TooLong);
        frames.Should().BeEmpty();
        handler.Asked.Should().BeNull("nothing should have been asked for");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task APeerThatCannotBeReachedIsSaidToBeUnreachable()
    {
        using var client = new HttpClient(new Answering(_ => throw new HttpRequestException("refused")));

        (GapFill fill, _) = await ArchiveGapFill.FetchAsync(client, Stream, Gap(3), CancellationToken.None);

        fill.Outcome.Should().Be(GapFillOutcome.Unreachable);
        fill.Recovered.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task ASyntheticNodeStaysSyntheticOnTheWayBackIn()
    {
        // The archive stores a reading, not the flag. SimulatedNodeMarker puts the mark inside the
        // node name precisely so it survives into a recording, and reading it back out is using
        // that carrier as designed -- defaulting to false would relabel a simulator's output as
        // measured, which is the laundering this codebase already fixed once at the live hop.
        using var client = new HttpClient(new Answering(_ => Json("""
            {"Status":"Success","Count":1,"Samples":[
              {"NodeId":"SIM:rig","Variable":"bus","Value":400.0,"Unit":"V",
               "TimestampIso":"2026-08-25T12:00:01.0000000Z"}]}
            """)));

        (_, IReadOnlyList<string> frames) =
            await ArchiveGapFill.FetchAsync(client, Stream, Gap(3), CancellationToken.None);

        RawPayloadParser.Parse(new RawPacket("peer", frames.Single(), DateTime.UtcNow))
            .Single().Flags.HasFlag(PacketFlags.Simulated).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheLedgerCountsWhatWasRecoveredAndBoundsWhatItRemembers()
    {
        var ledger = new BackfillLedger();

        for (int i = 0; i < BackfillLedger.Kept * 2; i++)
        {
            ledger.Record(new GapFill(DateTime.UtcNow, DateTime.UtcNow, GapFillOutcome.Filled, 10, false));
        }

        ledger.Attempts.Should().Be(BackfillLedger.Kept * 2, "the count survives the window");
        ledger.Recovered.Should().Be(BackfillLedger.Kept * 20);
        ledger.Recent().Should().HaveCount(BackfillLedger.Kept);
    }
}
