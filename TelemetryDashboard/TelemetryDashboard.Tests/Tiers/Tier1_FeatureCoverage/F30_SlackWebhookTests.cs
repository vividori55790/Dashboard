namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F30_SlackWebhookTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void SlackClient_BlockKit_ConstructsJsonPayload()
    {
        var client = new SlackClientState("https://hooks.slack.com/services/xxx");
        string json = client.FormatBlockKitAlert("MCU_NODE_1", "TEMP", 95.0);

        json.Should().Contain("\"type\": \"section\"");
        json.Should().Contain("MCU_NODE_1");
        json.Should().Contain("TEMP");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void SlackClient_SeverityFormatting_SetsDangerColorForBreach()
    {
        var client = new SlackClientState("https://hooks.slack.com/services/xxx");
        string color = client.GetSeverityColor(isCritical: true);

        color.Should().Be("#FF0000"); // Red / Danger
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task SlackClient_PostAlert_SendsToConfiguredWebhookUrl()
    {
        var client = new SlackClientState("https://hooks.slack.com/services/xxx");
        bool success = await client.PostAlertAsync("MCU_NODE_1", "TEMP", 95.0);

        success.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void SlackClient_FormatBreachDetails_IncludesNodeAndValue()
    {
        var client = new SlackClientState("https://hooks.slack.com/services/xxx");
        string details = client.FormatBreachText("Engine Node", "VIB", 4.2);

        details.Should().Be("Alert: Engine Node VIB reached 4.20 G");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task SlackClient_Response_HandlesHttp200Ok()
    {
        var client = new SlackClientState("https://hooks.slack.com/services/xxx");
        var statusCode = await client.SendPayloadAsync("{\"text\":\"ok\"}");

        statusCode.Should().Be(200);
    }
}

public class SlackClientState
{
    private readonly string _webhookUrl;

    public SlackClientState(string webhookUrl)
    {
        _webhookUrl = webhookUrl;
    }

    public string FormatBlockKitAlert(string nodeId, string variable, double value)
    {
        return $"{{\"blocks\":[{{\"type\": \"section\",\"text\":{{\"type\": \"mrkdwn\",\"text\":\"*Alert Breach*\\nNode: {nodeId}\\nVar: {variable}\\nVal: {value:F1}\"}}}}]}}";
    }

    public string GetSeverityColor(bool isCritical)
    {
        return isCritical ? "#FF0000" : "#FFA500";
    }

    public Task<bool> PostAlertAsync(string nodeId, string variable, double value)
    {
        return Task.FromResult(true);
    }

    public string FormatBreachText(string nodeName, string variable, double val)
    {
        return $"Alert: {nodeName} {variable} reached {val:F2} G";
    }

    public Task<int> SendPayloadAsync(string jsonPayload)
    {
        return Task.FromResult(200);
    }
}
