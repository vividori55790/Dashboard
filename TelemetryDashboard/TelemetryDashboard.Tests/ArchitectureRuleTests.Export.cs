using System.IO;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Keeps the console's setup panel honest about what it is handing the operator.
/// </summary>
/// <remarks>
/// ROADMAP W3 asks for a panel that hands over the scrape config and the dashboard file with
/// nothing to type. The risk in a panel like that is not that it fails — it is that it succeeds
/// loudly while handing over something empty. A dashboard with no panels, a scrape config aimed at
/// an endpoint this host does not serve, and a rig that genuinely has nothing on it all present the
/// same way unless the panel is made to distinguish them.
/// <para>
/// Driven in a browser against a live <c>--simulate</c> host rather than a DOM stub, and against a
/// host with no ingest at all for the two silent states.
/// </para>
/// </remarks>
public partial class ArchitectureRuleTests
{
    private static string GrafanaExportSource() =>
        File.ReadAllText(Path.Combine(
            SolutionRoot, "TelemetryDashboard.Core", "Services", "GrafanaDashboardExport.cs"));

    [Fact]
    [Trait("Category", "Architecture")]
    public void TheGeneratedDashboardIsBuiltFromWhatReportedRatherThanFromWhatWasDeclared()
    {
        // The rule the workstream is judged against, pinned at the source rather than only in the
        // behaviour tests: this host also holds declared sets -- computed expressions and limit
        // rules -- whose channels may never have produced a value, and reaching for one of those
        // is the change that would quietly fill the dashboard with empty graphs.
        string generator = GrafanaExportSource();

        generator.Should().Contain("inventory.Channels()",
            "the input inventory only ever holds a channel because a frame carrying it arrived");
        generator.Should().Contain("Samples > 0",
            "and the rule is enforced here rather than inherited from what the inventory happens "
            + "to do today");

        generator.Should().NotContain("server.Computed");
        generator.Should().NotContain("LimitMonitor",
            "a declaration is not a reading, and generating from one is the obvious wrong "
            + "implementation of this feature");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void TheSetupPanelSeparatesNobodyCountingFromNothingArriving()
    {
        // As a file the two are identical -- a dashboard with no graphs -- so if the panel does not
        // draw the distinction nothing else will. Same rule as the input and fleet panels, at the
        // point where the answer leaves this process.
        string page = StreamClientSource();

        page.Should().Contain("inputs.tracking !== true",
            "the panel has to branch on it, not merely receive it");
        page.Should().Contain("채널이 없다는 뜻이 아닙니다",
            "the no-inventory case has to say what its emptiness does not mean");
        page.Should().Contain("하나도 도착하지 않았습니다",
            "and the tracking-but-silent case is a different sentence");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void TheOperatorIsToldThePanelsCoverOnlyChannelsThatHaveReported()
    {
        // Without this the panel is a generator an operator has no reason to trust: a channel that
        // starts reporting after the export is simply absent, and nothing on screen says why.
        string page = StreamClientSource();

        page.Should().Contain("보고한 적 있는 채널만 패널이 됩니다");
        page.Should().Contain("스냅샷",
            "and that the file goes stale, since a dashboard is downloaded once and kept");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void TheScrapeTargetIsCheckedRatherThanAsserted()
    {
        // /metrics belongs to another workstream and may not exist on this host yet. A panel that
        // printed a correct scrape config and said nothing would send an operator to Grafana to
        // wait in front of an empty graph -- this product's central failure, relocated to a tool
        // this console cannot see into.
        string page = StreamClientSource();

        page.Should().Contain("fetch('/metrics')");
        page.Should().Contain("/metrics 없음",
            "and the absent case has to be a visibly different answer from the present one");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void TheFileHandedOverIsTheOneTheHostGenerated()
    {
        // A panel that re-serialised the dashboard would hand over its own rendering while
        // claiming to hand over the host's. It is also what makes the endpoint checkable: the file
        // saved here and a curl of the same path have to be the same bytes.
        string page = StreamClientSource();

        page.Should().Contain("/api/export/grafana");
        page.Should().Contain("new Blob([exportText]",
            "the bytes the endpoint sent, not a JSON.stringify of a parsed copy");
        page.Should().NotContain("JSON.stringify(dash");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void TheExportPathIsAdvertisedLikeEveryOtherEndpoint()
    {
        // /api/status lists what this host serves, and an endpoint missing from it is one nobody
        // discovers without reading the source.
        TelemetryDashboard.Core.Streaming.TelemetryStreamingServer.AdvertisedEndpoints
            .Should().Contain("/api/export/grafana");
    }
}
