using TelemetryDashboard.Core.Analytics.Detectors;
using TelemetryDashboard.Host.Configuration;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The file an operator edits to choose detectors and point at a model, and every way it refuses.
/// </summary>
/// <remarks>
/// Written against the same standard as <c>JsonChannelMapReader</c>: a configuration that
/// half-loaded would produce a host monitoring less than the operator believes, with no symptom
/// beyond alerts that never fire. Each refusal below has a test because each one is a silent
/// failure otherwise.
/// </remarks>
public class DetectorConfigurationTests
{
    private const string FullFile = """
    {
      "detectors": [
        { "type": "mad",    "id": "robust",   "channels": ["*.TEMP"], "window": 40, "threshold": 3.5 },
        { "type": "ewma",   "id": "level",    "window": 60, "lambda": 0.15, "threshold": 3.0 },
        { "type": "rate",   "id": "physical", "maxRatePerSecond": 25.0, "maxGapSeconds": 2.0 },
        { "type": "zscore", "id": "wide",     "window": 200, "threshold": 4.0 }
      ],
      "inference": {
        "runtime": "http",
        "endpoint": "http://127.0.0.1:9",
        "modelId": "psfb-autoencoder-v3",
        "window": 32,
        "threshold": 0.75,
        "timeoutMs": 250,
        "maxScoreAgeMs": 4000
      }
    }
    """;

    [Fact]
    public void AFullFileBuildsEveryDetectorItNames_WithTheOperatorsLabelsIntact()
    {
        DetectorConfiguration configuration = DetectorConfigurationReader.Parse(FullFile);

        configuration.Detectors.Should().HaveCount(4);
        configuration.Inference!.Runtime.Should().Be(InferenceRuntime.Http);
        configuration.Inference.ModelId.Should().Be("psfb-autoencoder-v3");
        configuration.Inference.TimeoutMs.Should().Be(250);

        DetectorPanel panel = DetectorFactory.CreatePanel(configuration);

        panel.Detectors.Should().HaveCount(4);
        panel.Detectors.Select(d => d.DetectorId).Should().BeEquivalentTo(new[]
        {
            "robust:mad/w40/t3.5",
            "level:ewma/n60/L0.15/k3",
            "physical:rate/max25ps/gap2s",
            "wide:zscore-rolling/w200/t4/n5"
        });
    }

    [Fact]
    public void ChannelPatternsFromTheFileDecideWhichDetectorSeesWhat()
    {
        DetectorPanel panel = DetectorFactory.CreatePanel(DetectorConfigurationReader.Parse(FullFile));

        panel.Detectors.Single(d => d.DetectorId.StartsWith("robust:", StringComparison.Ordinal))
            .CanHandle("NODE_1.PRESSURE").Should().BeFalse("it was pointed at *.TEMP only");

        panel.Detectors.Single(d => d.DetectorId.StartsWith("level:", StringComparison.Ordinal))
            .CanHandle("NODE_1.PRESSURE").Should().BeTrue("an omitted channel list means every channel");
    }

    [Theory]
    [InlineData("{ not json", "not valid JSON")]
    [InlineData("""{ "detectors": [ { "type": "wavelet" } ] }""", "is not one this build can construct")]
    [InlineData("""{ "detectors": [ { "type": "rate" } ] }""", "needs a positive 'maxRatePerSecond'")]
    [InlineData("""{ "detectors": [ { "type": "mad", "channels": [] } ] }""", "present but empty")]
    [InlineData("""{ "inference": { "runtime": "carrier-pigeon" } }""", "is not recognised")]
    [InlineData("""{ "inference": { "runtime": "http" } }""", "needs an 'endpoint'")]
    [InlineData("""{ "inference": { "runtime": "onnx" } }""", "needs a 'modelPath'")]
    public void EveryUnusableConfigurationIsRefusedLoudlyWithTheReason(string json, string expected)
    {
        Action parse = () => DetectorConfigurationReader.Parse(json);

        parse.Should().Throw<InvalidDataException>().WithMessage($"*{expected}*");
    }

    [Fact]
    public void AnAbsentFileIsNotAnError_ItMeansNoExtraDetectorsWereConfigured()
    {
        using var workspace = new TempWorkspace();

        DetectorConfiguration configuration =
            DetectorConfigurationReader.LoadOrNone(workspace.File("nothing-here.json"));

        configuration.IsEmpty.Should().BeTrue();
        DetectorFactory.CreatePanel(configuration).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void AFileOnDiskProducesTheSamePanelAsTheSameTextInMemory()
    {
        using var workspace = new TempWorkspace();
        string path = workspace.File(DetectorConfigurationReader.DefaultFileName);
        File.WriteAllText(path, FullFile);

        DetectorConfigurationReader.LoadOrNone(path).Detectors.Should().HaveCount(4);
    }

    // ---------------------------------------------------------------
    // Host assembly
    // ---------------------------------------------------------------

    [Fact]
    public async Task TheHostAssemblesTheFileDetectorsPlusTheConfiguredModel()
    {
        await using AnalyticsSetup setup =
            AnalyticsSetup.Build(DetectorConfigurationReader.Parse(FullFile), "test");

        setup.Panel.Detectors.Should().HaveCount(5, "four from the file plus the model client");
        setup.Inference.Should().NotBeNull();
        setup.Inference!.DetectorId.Should().Contain("psfb-autoencoder-v3");
        setup.Report.Should().Contain(line => line.Contains("5 detector(s) from test"));
        setup.Report.Should().Contain(line => line.Contains("psfb-autoencoder-v3"));
    }

    [Fact]
    public async Task AFileOnDiskChangesWhatTheHostJudgesWith_WithoutARebuild()
    {
        using var workspace = new TempWorkspace();
        string path = workspace.File(DetectorConfigurationReader.DefaultFileName);
        File.WriteAllText(path, FullFile);

        string? previous = Environment.GetEnvironmentVariable(AnalyticsSetup.PathVariable);
        try
        {
            Environment.SetEnvironmentVariable(AnalyticsSetup.PathVariable, path);

            // No argument: exactly what the host does at start-up.
            await using AnalyticsSetup setup = AnalyticsSetup.Load();

            setup.Panel.Detectors.Select(d => d.DetectorId).Should().Contain(
                new[] { "robust:mad/w40/t3.5", "physical:rate/max25ps/gap2s" });
            setup.Inference.Should().NotBeNull("the file named an http model and the host built a client for it");
        }
        finally
        {
            Environment.SetEnvironmentVariable(AnalyticsSetup.PathVariable, previous);
        }
    }

    [Fact]
    public void AnInProcessModelIsRefusedRatherThanAcceptedAndNeverRun()
    {
        const string onnx = """
        { "inference": { "runtime": "onnx", "modelPath": "models/psfb.onnx" } }
        """;

        Action build = () => AnalyticsSetup.Build(DetectorConfigurationReader.Parse(onnx), "test");

        build.Should().Throw<InvalidDataException>()
            .WithMessage("*no in-process inference runtime*")
            .And.Message.Should().Contain("IInferenceEndpoint",
                "the refusal has to name the seam, or it reads as a dead end rather than as work not done");
    }

    [Fact]
    public void AnEndpointThatIsNotAnHttpUrlIsRefused()
    {
        const string bad = """
        { "inference": { "runtime": "http", "endpoint": "ftp://models/psfb" } }
        """;

        Action build = () => AnalyticsSetup.Build(DetectorConfigurationReader.Parse(bad), "test");

        build.Should().Throw<InvalidDataException>().WithMessage("*absolute http(s) URL*");
    }

    [Fact]
    public void AnEmptyConfigurationProducesAHostThatJudgesWithTheBuiltInEngineAndNothingElse()
    {
        AnalyticsSetup setup = AnalyticsSetup.Build(DetectorConfiguration.None, "test");

        setup.Panel.IsEmpty.Should().BeTrue();
        setup.Inference.Should().BeNull();
        setup.Report.Should().Contain(line => line.Contains("no detectors configured"));
    }

    [Fact]
    public void TheConfigurationPathCanBeMovedWithoutARebuild()
    {
        string? previous = Environment.GetEnvironmentVariable(AnalyticsSetup.PathVariable);
        try
        {
            // Built rather than spelled. What this asserts is that the variable is used verbatim,
            // and a drive letter says that in a way only Windows can read -- the assertion still
            // held off Windows, by luck rather than design, because the string was opaque to both
            // sides. The next one like it will not be opaque, which is what F27 turned out to be.
            string moved = Path.Combine(Path.GetTempPath(), "plant", "detectors-line3.json");

            Environment.SetEnvironmentVariable(AnalyticsSetup.PathVariable, moved);
            AnalyticsSetup.ResolvePath().Should().Be(moved);

            Environment.SetEnvironmentVariable(AnalyticsSetup.PathVariable, null);
            AnalyticsSetup.ResolvePath().Should()
                .EndWith(DetectorConfigurationReader.DefaultFileName)
                .And.StartWith(AppContext.BaseDirectory, "the default sits beside the executable");
        }
        finally
        {
            Environment.SetEnvironmentVariable(AnalyticsSetup.PathVariable, previous);
        }
    }
}
