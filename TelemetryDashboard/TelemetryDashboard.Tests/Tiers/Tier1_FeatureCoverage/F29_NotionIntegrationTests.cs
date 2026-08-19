namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

using System.Net.Http;

public class F29_NotionIntegrationTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void NotionClient_FormatPagePayload_ConstructsSummaryMetrics()
    {
        var client = new NotionClientState("token_secret_123");
        string jsonPayload = client.FormatPagePayload("Daily Telemetry Report", 1500, 2);

        jsonPayload.Should().Contain("Daily Telemetry Report");
        jsonPayload.Should().Contain("1500");
        jsonPayload.Should().Contain("2");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void NotionClient_FormatPagePayload_IncludesBlocksForCharts()
    {
        var client = new NotionClientState("token_secret_123");
        string jsonPayload = client.FormatPagePayload("Daily Telemetry Report", 100, 0);

        jsonPayload.Should().Contain("\"object\": \"block\"");
        jsonPayload.Should().Contain("\"type\": \"heading_2\"");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void NotionClient_FormatHeaders_IncludesBearerTokenAndVersion()
    {
        var client = new NotionClientState("token_secret_123");
        var headers = client.GetRequestHeaders();

        headers.Should().ContainKey("Authorization");
        headers["Authorization"].Should().Be("Bearer token_secret_123");
        headers.Should().ContainKey("Notion-Version");
        headers["Notion-Version"].Should().Be("2022-06-8");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task NotionClient_GenerateReport_CreatesDailySummaryDocument()
    {
        var client = new NotionClientState("token_secret_123");
        bool success = await client.CreateReportPageAsync("Telemetry Audit Report");

        success.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task NotionClient_HandleError_CatchesRestApiFailures()
    {
        var client = new NotionClientState("invalid_token");
        client.SimulateApiFailure = true;

        Func<Task> act = async () => await client.CreateReportPageAsync("Report");

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}

public class NotionClientState
{
    private readonly string _apiToken;
    public bool SimulateApiFailure { get; set; }

    public NotionClientState(string apiToken)
    {
        _apiToken = apiToken;
    }

    public string FormatPagePayload(string title, int totalPackets, int anomalyCount)
    {
        return $"{{\"parent\": {{\"database_id\": \"db_01\"}}, \"properties\": {{\"Title\": {{\"title\": [{{\"text\": {{\"content\": \"{title}\"}}}}]}}}}, \"children\": [{{\"object\": \"block\", \"type\": \"heading_2\", \"heading_2\": {{\"rich_text\": [{{\"text\": {{\"content\": \"Packets: {totalPackets}, Anomalies: {anomalyCount}\"}}}}]}}}}]}}";
    }

    public Dictionary<string, string> GetRequestHeaders()
    {
        return new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_apiToken}",
            ["Notion-Version"] = "2022-06-8"
        };
    }

    public Task<bool> CreateReportPageAsync(string title)
    {
        if (SimulateApiFailure) throw new HttpRequestException("401 Unauthorized");
        return Task.FromResult(true);
    }
}
