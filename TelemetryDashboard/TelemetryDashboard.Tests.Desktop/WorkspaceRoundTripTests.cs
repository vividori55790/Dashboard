using System;
using System.IO;
using FluentAssertions;
using TelemetryDashboard.UI.Docking;
using Xunit;

namespace TelemetryDashboard.Tests.Desktop;

/// <summary>
/// Whether the operator's panel arrangement survives closing the application.
/// </summary>
/// <remarks>
/// It did not, and not because anything was missing. <c>LayoutManager</c> could serialise the dock
/// and read it back, <c>WorkspaceManager</c> could write that to a file, and <c>WorkspaceProfile</c>
/// carried a <c>LayoutXml</c> field for it — and no code in the application called any of the
/// three. The window came back the way it shipped at every launch.
/// <para>
/// The Tier1 tests that claimed to cover this exercised a <c>WorkspaceLayoutState</c> declared at
/// the bottom of the test file, so they would have stayed green through the deletion of everything
/// above.
/// </para>
/// </remarks>
public class WorkspaceRoundTripTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "tdws_" + Guid.NewGuid().ToString("N")[..10] + ".xml");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    /// <summary>A layout of the shape AvalonDock actually writes.</summary>
    private const string DockLayout =
        "<LayoutRoot><RootPanel Orientation=\"Horizontal\">"
        + "<LayoutDocumentPaneGroup><LayoutDocumentPane>"
        + "<LayoutDocument Title=\"실시간 파형\" ContentId=\"ScopeView\" IsSelected=\"False\" />"
        + "<LayoutDocument Title=\"스트리밍 서버\" ContentId=\"StreamingServerView\" IsSelected=\"True\" />"
        + "</LayoutDocumentPane></LayoutDocumentPaneGroup></RootPanel></LayoutRoot>";

    [Fact]
    [Trait("Category", "Tier1")]
    public void AnArrangementWrittenInOneSessionIsReadBackInTheNext()
    {
        var saved = new WorkspaceProfile { Name = "Last session", LayoutXml = DockLayout };

        WorkspaceStore.Save(saved, _path).Should().BeNull();
        WorkspaceProfile? loaded = WorkspaceStore.Load(_path);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Last session");
        loaded.LayoutXml.Should().Be(DockLayout,
            "the dock layout has to come back byte for byte -- it is the whole payload, and the "
            + "arrangement is lost if any of it is dropped in transit");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void WhichDocumentWasSelectedIsPartOfWhatIsRestored()
    {
        // The property this round-trip is verified against on the running window: select the second
        // document, close, reopen, and it is still selected.
        WorkspaceStore.Save(new WorkspaceProfile { LayoutXml = DockLayout }, _path);

        WorkspaceProfile? loaded = WorkspaceStore.Load(_path);

        loaded!.LayoutXml.Should().Contain("ContentId=\"StreamingServerView\" IsSelected=\"True\"");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void NothingSavedReadsAsNothingSavedRatherThanAsAnEmptyArrangement()
    {
        // Null, not a default profile. The caller has to be able to tell "the operator has never
        // arranged this" from "the operator arranged it into nothing", because the first means
        // leave the layout the XAML declares alone and the second would blank the window.
        WorkspaceStore.Load(_path).Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AProfileWithNoLayoutInItIsNotAnArrangement()
    {
        WorkspaceStore.Save(new WorkspaceProfile { Name = "Empty", LayoutXml = string.Empty }, _path);

        WorkspaceStore.Load(_path).Should().BeNull(
            "restoring an empty layout would replace the shipped arrangement with nothing");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ADamagedFileFallsBackRatherThanTakingTheWindowWithIt()
    {
        File.WriteAllText(_path, "<WorkspaceProfile><this is not well formed");

        // WorkspaceManager answers a parse failure with a default profile, whose LayoutXml is
        // empty -- so the store reports nothing to restore, and the window keeps the arrangement it
        // was built with. The alternative is an application that will not open.
        WorkspaceStore.Load(_path).Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ClearingForgetsTheArrangementAndSaysWhetherThereWasOne()
    {
        WorkspaceStore.Clear(_path).Should().BeFalse("there is nothing there yet");

        WorkspaceStore.Save(new WorkspaceProfile { LayoutXml = DockLayout }, _path);

        WorkspaceStore.Clear(_path).Should().BeTrue();
        File.Exists(_path).Should().BeFalse();
        WorkspaceStore.Load(_path).Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheLayoutSurvivesTheXmlEscapingItGoesThrough()
    {
        // LayoutXml is XML stored inside an XElement, so it is escaped on the way in and unescaped
        // on the way out. A layout with Korean titles and quoted attributes is exactly the case
        // where a half-done job produces a file that loads and describes a different arrangement.
        var profile = new WorkspaceProfile { LayoutXml = DockLayout };

        string xml = LayoutProfileSerializer.SaveToXml(profile);
        WorkspaceProfile restored = LayoutProfileSerializer.LoadFromXml(xml);

        restored.LayoutXml.Should().Be(DockLayout);
        restored.LayoutXml.Should().Contain("실시간 파형");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ADockWithNoManagerAttachedReportsNoLayoutRatherThanAnEmptyOne()
    {
        // What the saver checks for before writing. The serialiser answers with this placeholder
        // when it has nothing to serialise, and storing it would replace a good arrangement with
        // an empty one at the next clean shutdown.
        string xml = new LayoutManager().SaveLayoutToXml();

        xml.Should().Be("<AvalonDockLayout></AvalonDockLayout>");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AnUnreadableLayoutIsRefusedAndSaysWhy()
    {
        var manager = new LayoutManager();

        manager.LoadLayoutFromXml("<MalformedXml").Should().BeFalse();
        manager.LoadLayoutFromXml(string.Empty).Should().BeFalse();
    }
}
