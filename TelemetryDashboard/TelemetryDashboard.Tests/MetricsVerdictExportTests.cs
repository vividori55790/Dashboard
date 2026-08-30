using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Tests;

/// <summary>
/// This host's own judgements do not leave as measurements.
/// </summary>
/// <remarks>
/// Found by scraping a live host after the metrics endpoint was merged. The response carried
/// <c>telemetry_channel_value{channel="SIM:generic-machine.ambient.temperature.predicted"}</c>
/// — a forecast, with nothing on it to say so, addressed to whatever Prometheus and Grafana a site
/// already runs. <c>predictedHorizonSec</c> went out the same way, as a "reading" of 2.
/// <para>
/// It arrived honestly. <c>TelemetryFrameRecorder</c> records every numeric field of a frame as its
/// own series, so a scored channel acquires those beside it, and the console labels them. The
/// endpoint exported the store faithfully. Faithful export is the wrong contract here: a consumer
/// downstream has no labels and no context, and ARCHITECTURE's worked example is about this exact
/// number — the forecast was withheld for 92% of channels because a fitted line explaining nothing
/// is not a prediction. Publishing the surviving 8% as an instrument reading undoes that at the one
/// boundary where the withholding cannot be seen.
/// </para>
/// </remarks>
public class MetricsVerdictExportTests
{
    private static TelemetryStreamingServer ServerWithAScoredChannel()
    {
        var server = new TelemetryStreamingServer(port: 0);

        server.PublishSample("RIG-01.bus_voltage", 401.0);
        server.PublishSample("RIG-01.bus_voltage.predicted", 512.0);
        server.PublishSample("RIG-01.bus_voltage.predictedHorizonSec", 2.0);
        server.PublishSample("RIG-01.bus_voltage.anomalyScore", 2.7);

        return server;
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AForecastDoesNotLeaveAsAReading()
    {
        string exposition = MetricsEndpoint.Render(ServerWithAScoredChannel());

        exposition.Should().Contain("node=\"RIG-01\",channel=\"bus_voltage\"",
            "the measurement itself still goes out -- excluding everything would pass this test "
            + "for the wrong reason");

        exposition.Should().NotContain("bus_voltage.predicted",
            "a forecast presented to somebody else's alerting rules as a measurement is this "
            + "product's central failure, exported");
        exposition.Should().NotContain("bus_voltage.anomalyScore",
            "and a verdict about a channel is not a reading of one");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheHorizonIsNotAReadingEither()
    {
        // Two seconds, exported as though an instrument had measured two of something. It is the
        // qualifier that makes the forecast interpretable, and on its own it is not a quantity at
        // all -- which is also why excluding the forecast without it would have been half a fix.
        MetricsEndpoint.Render(ServerWithAScoredChannel())
            .Should().NotContain("predictedHorizonSec");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AChannelMerelyNamedLikeAVerdictIsStillExported()
    {
        // The suffix match is on the last component, not on the name containing the word. A rig
        // with a channel genuinely called "predicted_load" is measuring something, and dropping it
        // would be this fix committing the opposite error -- withholding a measurement.
        var server = new TelemetryStreamingServer(port: 0);
        server.PublishSample("RIG-01.predicted_load", 12.5);
        server.PublishSample("RIG-01.anomalyScore_setpoint", 3.0);

        string exposition = MetricsEndpoint.Render(server);

        exposition.Should().Contain("channel=\"predicted_load\"");
        exposition.Should().Contain("channel=\"anomalyScore_setpoint\"");
    }
}
