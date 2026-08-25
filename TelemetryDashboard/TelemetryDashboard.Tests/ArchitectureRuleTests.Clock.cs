using System.Text.RegularExpressions;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The clock-offset estimator is unreachable, and the document has to keep saying so.
/// </summary>
/// <remarks>
/// ARCHITECTURE.md carried "Clock offset across nodes | Built | ... behind <c>/api/aligned</c>" for
/// a long time and it was wrong. <c>/api/aligned</c> does construct the buffer, and it aligns
/// channels drawn from one host's own series store — which share one clock and need no offset.
/// <c>SyncNodeClock</c> itself has never had a caller outside a test.
/// <para>
/// The reachability ratchet could not catch this. It works on types, and
/// <c>TimeSyncJitterBuffer</c> <em>is</em> constructed, so the type is reachable while half its
/// surface is not. A method nobody calls inside a class somebody does is invisible to it, and that
/// is exactly the shape this claim had.
/// </para>
/// <para>
/// So this rule checks the two against each other rather than pinning either. Wire the estimator
/// and it fails, naming the row to update — which is the right moment to update it, because the
/// document is only wrong in the window between the wiring and somebody remembering.
/// </para>
/// </remarks>
public partial class ArchitectureRuleTests
{
    private const string NotWiredRow = "| Clock offset across nodes | **Not wired** |";

    private static string ArchitectureDocument() =>
        File.ReadAllText(Path.Combine(
            Directory.GetParent(SolutionRoot)?.FullName ?? SolutionRoot, "ARCHITECTURE.md"));

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheDocumentAgreesWithWhetherTheClockEstimatorIsCalled()
    {
        var call = new Regex(@"\.\s*SyncNodeClock\s*\(", RegexOptions.Singleline);

        string[] callers = ProductionSourceFiles()
            .Where(file => call.IsMatch(StripComments(File.ReadAllText(file))))
            .Select(Relative)
            .ToArray();

        bool documentSaysUnwired = ArchitectureDocument().Contains(NotWiredRow, StringComparison.Ordinal);

        if (callers.Length == 0)
        {
            documentSaysUnwired.Should().BeTrue(
                "nothing in the product calls SyncNodeClock, so every offset it could report is "
                + "zero on every path a running program takes. The row in ARCHITECTURE.md must "
                + "keep saying so -- it read 'Built ... behind /api/aligned' for a long time, "
                + "which was the buffer being constructed mistaken for its clock half running");
            return;
        }

        documentSaysUnwired.Should().BeFalse(
            "the estimator now has a caller and the document still calls it unwired. Update the "
            + "'Clock offset across nodes' row and §3's prose, then say what the offset is "
            + "measured against and how the uncertainty behaved on a real link:\n"
            + string.Join("\n", callers));
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void NothingReportsAnOffsetWithoutTheErrorBarBesideIt()
    {
        // The point of ClockOffsetEstimate is that the two travel together. A caller that reaches
        // past it for the scalar has recovered the exact defect it replaced -- a point estimate
        // read as a guarantee -- and would do it in a place nobody is looking, because the type
        // still appears in the signature.
        var offsetOnly = new Regex(@"GetClockOffset\s*\([^)]*\)\s*\.\s*OffsetSec");

        string[] offenders = ProductionSourceFiles()
            .Where(file => offsetOnly.IsMatch(StripComments(File.ReadAllText(file))))
            .Select(Relative)
            .ToArray();

        offenders.Should().BeEmpty(
            "read the estimate, check IsBounded or ask CanOrder, and only then use the number. "
            + "An offset taken straight off the call is a point estimate with its error bar "
            + "discarded one character after it was computed:\n" + string.Join("\n", offenders));
    }
}
