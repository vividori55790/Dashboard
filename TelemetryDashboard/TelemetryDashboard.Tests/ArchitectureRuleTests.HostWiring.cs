using TelemetryDashboard.Host.Startup;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Rules that keep a wired feature wired.
/// </summary>
/// <remarks>
/// <c>EveryDeclaredContractHasAnImplementation</c> catches a contract nothing implements. It does
/// not catch the failure that followed: an implementation nothing constructs.
/// <c>PluginHostContext</c> and <c>ManifestIndexMarketplace</c> were both complete, correct and
/// referenced by zero production code, so the plugin surface and the extension catalogue were
/// features the codebase claimed and could not perform — and every test stayed green, because the
/// tests held mocks.
///
/// A type is reachable only if something starts it, and in a console application the only thing
/// that starts anything is the entry point. This rule reads it.
/// </remarks>
public partial class ArchitectureRuleTests
{
    [Fact]
    [Trait("Category", "Architecture")]
    public void HostEntryPointStartsThePluginHostAndTheExtensionCatalogue()
    {
        string entryPoint = Path.Combine(SolutionRoot, "TelemetryDashboard.Host", "Program.cs");
        File.Exists(entryPoint).Should().BeTrue();

        string source = File.ReadAllText(entryPoint);

        // Type names rather than method names, so a refactor that renames a step still passes and
        // only deleting the step fails. Deleting the step is the regression this rule is about.
        source.Should().Contain(nameof(PluginHostSession),
            "a plugin discovered on disk reaches the host only because the entry point starts the "
            + "session that builds its context; without this call PluginHostContext goes back to "
            + "being a type nothing constructs");

        source.Should().Contain(nameof(ExtensionCatalogueReport),
            "the extension catalogue has exactly one entry point, and a marketplace nobody calls "
            + "cannot list anything");
    }
}
