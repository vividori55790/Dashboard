using TelemetryDashboard.Core.Recording;
using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Tests;

/// <summary>
/// A frame's verdict belongs to its reading, and to nothing else in the frame.
/// </summary>
/// <remarks>
/// The recorder writes every numeric field of a frame as its own DVR series — the reading under the
/// channel's own name, and the forecast and the horizon beside it. It gave all of them the same
/// verdict: the frame's single <c>anomalyScore</c>, which is the analyzer's judgement about the
/// <em>reading</em>. So a replay pulled a week later shows <c>bus_voltage.predicted</c> flagged as
/// anomalous whenever <c>bus_voltage</c> was, and carrying that channel's sigma as its own.
/// <para>
/// It is the shape ARCHITECTURE §7 names when a peer's score is ingested and scored again — a
/// verdict about one thing presented as a verdict about another — reached here without a network,
/// inside one process, by a loop that had one score in scope and several values.
/// </para>
/// <para>
/// Found while building the metrics endpoint, which was made to refuse those series outright. That
/// stopped them leaving the host and left the recording wrong.
/// </para>
/// </remarks>
public class DvrVerdictScopeTests
{
    private const string ScoredFrame = """
        {"timestamp":"2026-08-25T02:40:00.0000000Z","nodeId":"RIG-01","variable":"bus_voltage",
         "value":401.0,"unit":"V","anomalyScore":3.4,"isAnomaly":true,
         "predicted":512.0,"predictedHorizonSec":2.0}
        """;

    private static Dictionary<string, Core.Models.DvrFrame> Recorded()
    {
        var dvr = new TimeTravelDvrPlayer();
        TelemetryFrameRecorder.Record(dvr, ScoredFrame);

        return dvr.GetFramesInRange(0, double.MaxValue)
            .GroupBy(frame => frame.ChannelName)
            .ToDictionary(group => group.Key, group => group.Last());
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheReadingKeepsTheVerdictThatWasReachedAboutIt()
    {
        // The positive control. Removing the verdict from everything would satisfy the test below
        // and destroy the recording's whole point.
        Dictionary<string, Core.Models.DvrFrame> frames = Recorded();

        frames.Should().ContainKey("RIG-01.bus_voltage");
        Core.Models.DvrFrame reading = frames["RIG-01.bus_voltage"];

        reading.HasVerdict.Should().BeTrue();
        reading.ZScore.Should().Be(3.4);
        reading.IsAnomaly.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AForecastDoesNotInheritTheReadingsVerdict()
    {
        Dictionary<string, Core.Models.DvrFrame> frames = Recorded();

        frames.Should().ContainKey("RIG-01.bus_voltage.predicted",
            "the forecast is still recorded -- it is worth having in a replay, and dropping it "
            + "would be fixing the wrong half");

        Core.Models.DvrFrame forecast = frames["RIG-01.bus_voltage.predicted"];

        forecast.HasVerdict.Should().BeFalse(
            "3.4 sigma is what the analyzer concluded about a bus reading of 401 V. Attaching it "
            + "to a forecast of 512 V says the forecast was examined and found unusual, and "
            + "nothing examined the forecast at all");
        forecast.Value.Should().Be(512.0, "the number itself is unchanged; only the claim about it is");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void NorDoesTheHorizonThatQualifiesIt()
    {
        // Two seconds, recorded as a 3.4-sigma anomaly. The horizon is not a quantity anything
        // could be anomalous about.
        Recorded()["RIG-01.bus_voltage.predictedHorizonSec"].HasVerdict.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AFrameWithNoVerdictStillLeavesTheReadingUnjudged()
    {
        // The pre-existing rule this must not disturb: a warm-up frame carries no score, and it is
        // recorded without a verdict rather than with one of zero.
        var dvr = new TimeTravelDvrPlayer();
        TelemetryFrameRecorder.Record(dvr,
            """{"nodeId":"RIG-01","variable":"bus_voltage","value":401.0,"unit":"V"}""");

        dvr.GetFramesInRange(0, double.MaxValue).Single()
            .HasVerdict.Should().BeFalse();
    }
}
