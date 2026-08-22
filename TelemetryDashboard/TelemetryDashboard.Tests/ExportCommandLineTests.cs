using TelemetryDashboard.Host.Archive;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Reading <c>export</c>'s command line, and refusing what cannot be read.
/// </summary>
/// <remarks>
/// Every refusal here costs an operator one retyped command. The default in
/// <see cref="TheDefaultSelectionIsEverything"/> costs them their data: a query layer default of
/// 1000, silently accepted, exports the first sixteen minutes of an overnight recording and prints
/// that it succeeded.
/// </remarks>
public class ExportCommandLineTests
{
    private static ExportCommandLine Parse(params string[] args) =>
        ExportCommandLine.Parse(args);

    private static ExportCommandLine Good(params string[] tail) =>
        Parse(new[] { "export", "bench.db", "--out", "bench.mat" }.Concat(tail).ToArray());

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheDefaultSelectionIsEverything()
    {
        ExportCommandLine command = Good();

        command.Error.Should().BeNull();
        command.Filter.Limit.Should().Be(int.MaxValue,
            "the query layer's own default of 1000 would truncate a long recording silently");
        command.Filter.StartTime.Should().BeNull();
        command.Filter.EndTime.Should().BeNull();
        command.Filter.NodeId.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AnExportWithNowhereToWriteIsRefused()
    {
        Parse("export", "bench.db").Error.Should().Contain("--out is required");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AnExportWithNothingToReadIsRefused()
    {
        Parse("export", "--out", "bench.mat").Error.Should().Contain("archive to export is required");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AWindowThatEndsBeforeItBeginsIsRefusedRatherThanExportedAsEmpty()
    {
        // An empty result and an impossible request are different things, and only one of them is
        // worth widening a window over.
        ExportCommandLine command =
            Good("--from", "2026-08-21T15:00:00Z", "--to", "2026-08-21T14:00:00Z");

        command.Error.Should().Contain("--from is after --to");
    }

    [Theory]
    [Trait("Category", "Tier1")]
    [InlineData("yesterday")]
    [InlineData("2026-13-45")]
    public void ATimestampNobodyCanReadIsRefusedWithAnExampleOfOneThatWorks(string raw)
    {
        ExportCommandLine command = Good("--from", raw);

        command.Error.Should().Contain("ISO-8601").And.Contain("2026-08-21T14:00:00Z");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ATimeWithNoDateIsRefusedRatherThanAnsweredWithToday()
    {
        // DateTime.TryParse reads "14:00" and fills in today's date. On an archive recorded
        // yesterday that is a window over a day it never covered -- and the export succeeds,
        // reporting an empty result as though the rig had been idle.
        ExportCommandLine command = Good("--from", "14:00");

        command.Error.Should().Contain("carrying a date");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AStampWithNoZoneIsReadAsUtcTheWayTheHistoryEndpointReadsOne()
    {
        // The console and this command have to select the same window from the same words, or an
        // operator checking one against the other is comparing two different requests.
        ExportCommandLine command = Good("--from", "2026-08-21T14:00:00");

        command.Error.Should().BeNull();
        command.Filter.StartTime.Should().Be(new DateTime(2026, 8, 21, 14, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void VariableIsAcceptedForChannelBecauseThatIsWhatTheApiCallsIt()
    {
        Good("--variable", "Vout").Filter.Variable.Should().Be("Vout");
        Good("--channel", "Vout").Filter.Variable.Should().Be("Vout");
    }

    [Theory]
    [Trait("Category", "Tier2")]
    [InlineData("--limit", "0", "positive whole number")]
    [InlineData("--limit", "-4", "positive whole number")]
    [InlineData("--limit", "many", "positive whole number")]
    [InlineData("--bogus", "x", "unknown argument")]
    public void AnArgumentThatCannotBeActedOnIsRefused(string flag, string value, string expected)
    {
        Good(flag, value).Error.Should().Contain(expected);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ASecondArchiveIsRefusedRatherThanSilentlyReplacingTheFirst()
    {
        Parse("export", "one.db", "two.db", "--out", "x.mat")
            .Error.Should().Contain("only one archive");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void HelpIsAnAnswerRatherThanAnError()
    {
        ExportCommandLine command = Parse("export", "--help");

        command.ShowHelp.Should().BeTrue();
        command.Error.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void APathWithNoExtensionGetsTheOneTheExporterWrites()
    {
        ExportDestination.TryResolve("bench", out string target, out string? refusal).Should().BeTrue();

        refusal.Should().BeNull();
        target.Should().EndWith(MatlabArchiveExporter.Extension);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void APathNamingAFormatThisCannotWriteIsRefusedRatherThanRenamed()
    {
        // Writing a MAT-file called bench.csv would answer a different question under the name of
        // the one that was asked.
        ExportDestination.TryResolve("bench.csv", out _, out string? refusal).Should().BeFalse();

        refusal.Should().Contain(".csv").And.Contain(MatlabArchiveExporter.Extension);
    }
}
