using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Parsers;
using TelemetryDashboard.Core.Services;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

/// <summary>Assembly-level structure checks for the portable half of the solution.</summary>
/// <remarks>
/// The fifth F01 case — <c>UIAssembly_LoadsSuccessfully_HasWpfTarget</c> — now lives in
/// TelemetryDashboard.Tests.Desktop. It was the only one that loaded the WPF shell, and keeping it
/// here would have kept these four Core/Infrastructure/Plugins assertions Windows-only for no
/// reason of their own.
/// </remarks>
public class F01_SolutionStructureTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void CoreAssembly_LoadsSuccessfully_HasRequiredNamespaces()
    {
        var type = typeof(TelemetryPacket);
        type.Assembly.FullName.Should().Contain("TelemetryDashboard.Core");
        type.Namespace.Should().Be("TelemetryDashboard.Core.Models");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void InfrastructureAssembly_LoadsSuccessfully_ReferencesCore()
    {
        var type = typeof(TelemetryDashboard.Infrastructure.Serial.MultiPortSerialManager);
        type.Assembly.FullName.Should().Contain("TelemetryDashboard.Infrastructure");
        var referencedAssemblies = type.Assembly.GetReferencedAssemblies();
        referencedAssemblies.Should().Contain(a => a.Name == "TelemetryDashboard.Core");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void PluginsAssembly_LoadsSuccessfully_ContainsPluginContracts()
    {
        var type = typeof(TelemetryDashboard.Core.Interfaces.IPlugin);
        type.Assembly.FullName.Should().Contain("TelemetryDashboard.Core");
        var samplePluginType = typeof(TelemetryDashboard.Plugins.SamplePlugins.SampleTelemetryPlugin);
        samplePluginType.Assembly.FullName.Should().Contain("TelemetryDashboard.Plugins");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Solution_AllFourProjects_InstantiateCleanly()
    {
        var packet = new TelemetryPacket("NODE_1", "TEMP", 25.5, "C");
        var mockManager = new Mock<ISerialManager>();
        var samplePlugin = new TelemetryDashboard.Plugins.SamplePlugins.SampleTelemetryPlugin();

        packet.Should().NotBeNull();
        mockManager.Object.Should().NotBeNull();
        samplePlugin.Should().NotBeNull();
        samplePlugin.Name.Should().NotBeNullOrEmpty();
    }
}
