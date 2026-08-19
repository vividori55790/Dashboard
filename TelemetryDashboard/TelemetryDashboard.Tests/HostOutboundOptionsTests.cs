using System;
using System.Linq;
using FluentAssertions;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Host.Startup;
using TelemetryDashboard.Infrastructure.Updater;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Covers the command line and start-up reporting for the host's outbound options.
/// </summary>
public class HostOutboundOptionsTests
{
    private static HostOptions Parse(params string[] args) =>
        CommandLineParser.Parse(args, new HostOptions());

    [Fact]
    public void ABrokerAddressWithoutAPortTakesTheDefault()
    {
        HostOptions options = Parse("--mqtt", "broker.plant.local");

        options.Error.Should().BeNull();
        options.MqttBrokerHost.Should().Be("broker.plant.local");
        options.MqttBrokerPort.Should().Be(HostOptions.DefaultMqttPort);
    }

    [Fact]
    public void ABrokerAddressWithAPortUsesIt()
    {
        HostOptions options = Parse("--mqtt", "10.0.0.4:8883");

        options.MqttBrokerHost.Should().Be("10.0.0.4");
        options.MqttBrokerPort.Should().Be(8883);
    }

    [Fact]
    public void AMistypedBrokerPortIsRefusedRatherThanQuietlyDefaulted()
    {
        // Defaulting would connect to 1883 while the operator believed they had named another port.
        Parse("--mqtt", "broker:not-a-port").Error.Should().Contain("broker address");
    }

    [Fact]
    public void ATopicPrefixMayNotSmuggleInAnMqttWildcard()
    {
        Parse("--mqtt-topic", "plant/#").Error.Should().Contain("wildcard");
        Parse("--mqtt-topic", "plant/+").Error.Should().Contain("wildcard");
        Parse("--mqtt-topic", "plant/line3").Error.Should().BeNull();
    }

    [Fact]
    public void AWebhookThatIsNotAUrlIsRefused()
    {
        Parse("--slack-webhook", "hooks.slack.com/services/x").Error.Should().Contain("absolute webhook URL");
        Parse("--slack-webhook", "https://hooks.slack.com/services/T/B/x").Error.Should().BeNull();
    }

    [Fact]
    public void EveryOutboundOptionIsOffUnlessAskedFor()
    {
        HostOptions options = Parse("--simulate");

        options.SlackWebhook.Should().BeNull();
        options.MqttBrokerHost.Should().BeNull();
        options.UpdateRepository.Should().BeNull("a host must not report anywhere the operator did not name");
    }

    [Fact]
    public void TheHelpScreenDocumentsEveryOutboundOptionAndItsEnvironmentVariable()
    {
        string usage = UsageText.Render();

        foreach (string expected in new[]
                 {
                     "--slack-webhook", "--mqtt", "--mqtt-topic", "--check-updates",
                     EnvironmentVariables.SlackWebhook, EnvironmentVariables.MqttBroker,
                     EnvironmentVariables.MqttTopic, EnvironmentVariables.CheckUpdates
                 })
        {
            usage.Should().Contain(expected);
        }
    }

    [Fact]
    public void AnAvailableUpdateIsReportedAndExplicitlyNotApplied()
    {
        string[] lines = UpdateCheck.Render("owner/repo", new UpdateCheckResult
        {
            IsUpdateAvailable = true,
            LatestVersion = "2.1.0",
            CurrentVersion = "2.0.0",
            StatusMessage = "newer release available"
        });

        string report = string.Join("\n", lines);
        report.Should().Contain("2.1.0").And.Contain("2.0.0").And.Contain("owner/repo");
        report.Should().Contain("Nothing was downloaded or applied",
            "an update channel that installs on its own is a remote code execution path");
    }

    [Fact]
    public void AnUnreachableFeedReportsWhyRatherThanClaimingTheBuildIsCurrent()
    {
        string[] lines = UpdateCheck.Render("owner/repo", new UpdateCheckResult
        {
            IsUpdateAvailable = false,
            CurrentVersion = "2.0.0",
            StatusMessage = "Offline: could not reach the release feed (HttpRequestException)."
        });

        string.Join("\n", lines).Should().Contain("Offline").And.NotContain("up to date");
    }
}
