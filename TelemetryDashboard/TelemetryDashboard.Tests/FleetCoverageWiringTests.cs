using System.Runtime.CompilerServices;
using System.Text;
using TelemetryDashboard.Core.Cluster;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Streaming;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Host.Ingest;
using TelemetryDashboard.Host.Startup;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Telling the ledger what the rig is, and remembering it across a restart.
/// </summary>
/// <remarks>
/// <c>CoverageLedger</c> shipped able to answer three questions and was asked none of them. It
/// learned the nodes that spoke — the half that worked — while nothing declared the nodes that had
/// not, nothing remembered the learned set across a restart, and nothing retired a node removed on
/// purpose. Its own remarks named all three.
/// <para>
/// Driven on the running host: a run declaring PSFB-02 and GHOST-01 reported both as never seen at
/// <c>/api/status</c> while running. The host was then killed — not stopped — and restarted with a
/// different profile: it remembered four nodes, retired GHOST-01, and after the silence threshold
/// reported SIM:COM3 and SIM:computed as "silent 84s" while PSFB-02 stayed "never seen".
/// </para>
/// </remarks>
public sealed class FleetCoverageWiringTests : IDisposable
{
    private static readonly DateTimeOffset Yesterday = DateTimeOffset.UtcNow.AddDays(-1);

    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private sealed class SilentSource : ITelemetrySource
    {
        public string Origin => "TEST";
        public bool IsSimulated => true;
        public string Description => "a source that never speaks";
        public string PortName => "COM-TEST";

        public async IAsyncEnumerable<RawPacket> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield break;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static HostOptions Options(
        string? state = null, string[]? expect = null, string[]? retire = null) => new()
    {
        CoverageStatePath = state,
        ExpectedNodes = expect ?? [],
        RetiredNodes = retire ?? []
    };

    [Fact]
    [Trait("Category", "Tier1")]
    public void ANodeRememberedFromAPreviousRunIsSilentRatherThanNeverSeen()
    {
        // The difference between hardware that was never commissioned and hardware that stopped
        // yesterday, and they call for opposite things from whoever reads the report.
        var ledger = new CoverageLedger();
        ledger.Expect("PSFB-01", Yesterday);
        ledger.Expect("PSFB-99");

        CoverageSnapshot snapshot = ledger.Snapshot();

        snapshot.Missing.Single(n => n.NodeId == "PSFB-01").Presence.Should().Be(NodePresence.Silent);
        snapshot.Missing.Single(n => n.NodeId == "PSFB-99").Presence.Should().Be(NodePresence.NeverSeen);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ARestoredTimeNeverMovesALiveNodeBackwardsIntoSilence()
    {
        // Restore runs before ingest, but a state file written by a host whose clock ran ahead
        // would otherwise be able to overwrite a sample that has genuinely just arrived.
        var ledger = new CoverageLedger();
        ledger.RecordSample("PSFB-01");

        ledger.Expect("PSFB-01", Yesterday);

        ledger.Snapshot().Reporting.Should().ContainSingle().Which.NodeId.Should().Be("PSFB-01");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task TheFleetSurvivesARestartThatNobodyShutDownCleanly()
    {
        string state = _workspace.File("fleet.json");
        CoverageStateFile.Write(state, new[]
        {
            new KeyValuePair<string, DateTimeOffset?>("PSFB-01", Yesterday),
            new KeyValuePair<string, DateTimeOffset?>("DAB-01", null)
        }).Should().BeNull();

        await using var server = new TelemetryStreamingServer(18101);
        var pump = new TelemetryIngestPump(server, new SilentSource());

        CoverageSetup.Apply(Options(state: state), pump, server);

        pump.Coverage.KnownNodes.Should().BeEquivalentTo("PSFB-01", "DAB-01");
        server.Coverage.Should().NotBeNull("coverage has to be readable while the host runs");
        server.Coverage!().Missing.Should().HaveCount(2);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task RetiringANodeBeatsTheFileThatRemembersIt()
    {
        // Otherwise the state file resurrects a decommissioned node on every start, and the alarm
        // it raises can never be cleared.
        string state = _workspace.File("retire.json");
        CoverageStateFile.Write(state, new[]
        {
            new KeyValuePair<string, DateTimeOffset?>("OLD-RIG", Yesterday)
        });

        await using var server = new TelemetryStreamingServer(18102);
        var pump = new TelemetryIngestPump(server, new SilentSource());

        CoverageSetup.Apply(Options(state: state, retire: ["OLD-RIG"]), pump, server);

        pump.Coverage.KnownNodes.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AStateFileRoundTripsTheTimesAsWellAsTheNames()
    {
        string path = _workspace.File("round.json");
        CoverageStateFile.Write(path, new[]
        {
            new KeyValuePair<string, DateTimeOffset?>("B", Yesterday),
            new KeyValuePair<string, DateTimeOffset?>("A", null)
        });

        IReadOnlyList<CoverageStateEntry> read = CoverageStateFile.Read(path, out string? note);

        note.Should().BeNull();
        read.Select(e => e.Node).Should().Equal(new[] { "A", "B" },
            "sorted, so a diff of two runs is readable");
        read.Single(e => e.Node == "B").LastHeard.Should().BeCloseTo(Yesterday, TimeSpan.FromSeconds(1));
        read.Single(e => e.Node == "A").LastHeard.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheStateFileCarriesNoByteOrderMark()
    {
        // Twice now a mark has made a file this product wrote unreadable to a strict parser.
        string path = _workspace.File("bom.json");
        CoverageStateFile.Write(path, new[] { new KeyValuePair<string, DateTimeOffset?>("A", null) });

        File.ReadAllBytes(path).Take(3).Should().NotEqual(Encoding.UTF8.GetPreamble());
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AStateFileNobodyCanReadCostsTheMemoryAndNotTheRun()
    {
        string path = _workspace.File("broken.json");
        File.WriteAllText(path, "{ this is not the file you are looking for");

        IReadOnlyList<CoverageStateEntry> read = CoverageStateFile.Read(path, out string? note);

        read.Should().BeEmpty();
        note.Should().Contain("could not be read").And.Contain("empty fleet");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AnAbsentStateFileIsTheNormalFirstRunRatherThanAFault()
    {
        CoverageStateFile.Read(_workspace.File("never-written.json"), out string? note).Should().BeEmpty();
        note.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheFleetFlagsAreReadOffTheCommandLine()
    {
        HostOptions parsed = CommandLineParser.Parse(
            new[] { "--simulate", "--expect", "PSFB-01, DAB-01", "--retire", "OLD-RIG" },
            new HostOptions());

        parsed.Error.Should().BeNull();
        parsed.ExpectedNodes.Should().Equal(new[] { "PSFB-01", "DAB-01" }, "spacing is not an id");
        parsed.RetiredNodes.Should().Equal(new[] { "OLD-RIG" });
    }

    [Theory]
    [Trait("Category", "Tier2")]
    [InlineData("--expect")]
    [InlineData("--retire")]
    public void AListOfNodesThatNamesNoneIsRefused(string flag)
    {
        // ",," parses to an empty list, and a flag that quietly did nothing would leave an operator
        // believing they had declared a fleet.
        HostOptions parsed = CommandLineParser.Parse(
            new[] { "--simulate", flag, " , , " }, new HostOptions());

        parsed.Error.Should().Contain(flag).And.Contain("at least one node id");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ANodeNamedTwiceIsExpectedOnce()
    {
        NodeIdList.Parse("PSFB-01,psfb-01,DAB-01").Should().Equal(new[] { "PSFB-01", "DAB-01" });
    }
}
