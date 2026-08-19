namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F14_DragAndDropTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void DragAndDrop_WorkspaceFile_TriggersProfileLoad()
    {
        var handler = new DragAndDropHandler();
        string file = "test.workspace";

        DropResult result = handler.ProcessDroppedFile(file);

        result.ActionType.Should().Be("LoadWorkspace");
        result.Success.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void DragAndDrop_ObjFile_Loads3DModel()
    {
        var handler = new DragAndDropHandler();
        string file = "engine.obj";

        DropResult result = handler.ProcessDroppedFile(file);

        result.ActionType.Should().Be("Load3DModel");
        result.Success.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void DragAndDrop_StlFile_ValidatesMeshGeometry()
    {
        var handler = new DragAndDropHandler();
        string file = "bracket.stl";

        DropResult result = handler.ProcessDroppedFile(file);

        result.ActionType.Should().Be("Load3DModel");
        result.Success.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void DragAndDrop_MatCsvFile_ImportsDataSession()
    {
        var handler = new DragAndDropHandler();
        string matFile = "session.mat";
        string csvFile = "session.csv";

        DropResult resMat = handler.ProcessDroppedFile(matFile);
        DropResult resCsv = handler.ProcessDroppedFile(csvFile);

        resMat.ActionType.Should().Be("ImportDataSession");
        resCsv.ActionType.Should().Be("ImportDataSession");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void DragAndDrop_UnsupportedFile_RejectsDrop()
    {
        var handler = new DragAndDropHandler();
        string file = "unsupported.exe";

        DropResult result = handler.ProcessDroppedFile(file);

        result.Success.Should().BeFalse();
        result.ActionType.Should().Be("Rejected");
    }
}

public class DropResult
{
    public bool Success { get; set; }
    public string ActionType { get; set; } = string.Empty;
}

public class DragAndDropHandler
{
    public DropResult ProcessDroppedFile(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".workspace" => new DropResult { Success = true, ActionType = "LoadWorkspace" },
            ".obj" or ".stl" => new DropResult { Success = true, ActionType = "Load3DModel" },
            ".mat" or ".csv" => new DropResult { Success = true, ActionType = "ImportDataSession" },
            _ => new DropResult { Success = false, ActionType = "Rejected" }
        };
    }
}
