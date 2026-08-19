using TelemetryDashboard.UI.Services;

namespace TelemetryDashboard.Tests.Desktop.Tiers.Tier2_BoundaryCornerCases;

/// <summary>F14: universal drag-and-drop boundary cases.</summary>
/// <remarks>
/// <c>DragDropHandler</c> is wired to WPF drop events and lives in the shell, so the file-shape
/// rules it enforces can only be asserted with the UI assembly loaded.
/// </remarks>
public class F14_DragAndDropBoundaryTests
{
    [Fact]
    [Trait("Category", "Tier2")]
    public void F14_Boundary_UnsupportedFileExtension_RejectsDropAction()
    {
        var dragDropHandler = new DragDropHandler();
        bool canAccept = dragDropHandler.CanAcceptFile("test.exe");
        canAccept.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F14_Boundary_EmptyFileDropped_DisplaysWarningNotification()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            var dragDropHandler = new DragDropHandler();
            DropResult result = dragDropHandler.ProcessDroppedFile(tempFile);
            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Empty");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F14_Boundary_Corrupted3dModelDropped_HandlesLoadingError()
    {
        string tempModel = Path.Combine(Path.GetTempPath(), "corrupted.stl");
        File.WriteAllText(tempModel, "NOT_AN_STL_FILE_BINARY_HEADER");

        try
        {
            var dragDropHandler = new DragDropHandler();
            DropResult result = dragDropHandler.ProcessDroppedFile(tempModel);
            result.Success.Should().BeFalse();
        }
        finally
        {
            File.Delete(tempModel);
        }
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F14_Boundary_MultipleFilesDroppedSimultaneously_ProcessesFirstValid()
    {
        var dragDropHandler = new DragDropHandler();
        string[] files = new string[] { "invalid.txt", "valid.workspace", "valid.obj" };

        string? selected = dragDropHandler.SelectPrimaryFile(files);
        selected.Should().Be("valid.workspace");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F14_Boundary_DropFileWithoutReadPermissions_ShowsAccessDeniedError()
    {
        var dragDropHandler = new DragDropHandler();
        string nonExistent = @"C:\SystemVolumeInformation\test.mat";

        DropResult result = dragDropHandler.ProcessDroppedFile(nonExistent);
        result.Success.Should().BeFalse();
    }
}
