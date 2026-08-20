using TelemetryDashboard.Host.Outbound;
using TelemetryDashboard.Host.Startup;

namespace TelemetryDashboard.Host.Configuration;

/// <summary>
/// The <c>--help</c> screen.
/// </summary>
/// <remarks>
/// Kept beside <see cref="CommandLineParser"/> so an option cannot be added without the text that
/// documents it sitting one file away, and out of the parser so neither grows past the point where
/// it can be read in one pass.
/// </remarks>
public static class UsageText
{
    /// <summary>Renders the usage screen, including the environment variable for each option.</summary>
    public static string Render() =>
        $"""
        TelemetryDashboard headless host -- serves the telemetry backbone and the browser console
        on any platform .NET 8 runs on. No desktop shell, no Windows dependency.

        Usage:
          TelemetryDashboard.Host [options]
          TelemetryDashboard.Host {ExtensionCommandLine.Verb} <action> [arguments]

        The '{ExtensionCommandLine.Verb}' subcommand installs, enables, disables, removes and lists
        extensions, then exits without serving anything. Run it with no action for its own help.

        Options:
          -p, --port <n>        TCP port for the streaming server.
                                Default {HostOptions.DefaultPort}. Env: {EnvironmentVariables.Port}
          -w, --web-root <dir>  Directory of static console assets. Repeatable; searched in order.
                                Default: the directory holding the executable.
                                Env: {EnvironmentVariables.WebRoot} (path-separator delimited)
          -c, --client <file>   HTML file served at '/'. Default: the first known console file
                                found under a web root. Env: {EnvironmentVariables.Client}
          -s, --serial <port>   Serial port to open, e.g. COM3 or /dev/ttyUSB0.
                                Default: none -- the host runs with no hardware attached.
                                Env: {EnvironmentVariables.Serial}
          -b, --baud <n>        Speed of --serial. Default {HostOptions.DefaultBaudRate}.
                                Env: {EnvironmentVariables.Baud}
          -r, --record <dir>    Write a CSV recording of every ingested sample into <dir>.
                                Default: no recording. Env: {EnvironmentVariables.Record}
              --simulate        Run the virtual simulator instead of hardware. Every frame it
                                produces is marked simulated=true and its node id is prefixed
                                'SIM:'. Env: {EnvironmentVariables.Simulate}=1
              --plugin-dir <dir>
                                Directory scanned for plugin assemblies at start-up.
                                Default: 'plugins' beside the executable.
                                Env: {EnvironmentVariables.PluginDir}
              --extension-dir <dir>
                                Directory of installed extensions, managed by the '{ExtensionCommandLine.Verb}'
                                subcommand. Default: '{ExtensionLoader.DefaultDirectoryName}' beside the executable.
          -x, --extensions <loc>
                                URL or path of a JSON extension catalogue index. The host lists
                                what it contains and installs nothing: running a third party's
                                code is a separate decision. Env: {EnvironmentVariables.Extensions}
              --slack-webhook <url>
                                Post an alert to a Slack incoming webhook when a channel is judged
                                anomalous. At most one message per channel every
                                {SlackAlertRelay.DefaultCooldown.TotalMinutes:0} minutes; anything held back during that
                                quiet period is counted and reported with the next
                                message. Env: {EnvironmentVariables.SlackWebhook}
              --mqtt <host[:port]>
                                Republish every scored sample to an MQTT broker.
                                Default port {HostOptions.DefaultMqttPort}. Env: {EnvironmentVariables.MqttBroker}
              --mqtt-topic <prefix>
                                Topic prefix; each channel is published to
                                <prefix>/<node>/<variable>. Default '{HostOptions.DefaultMqttTopicPrefix}'.
                                Env: {EnvironmentVariables.MqttTopic}
              --check-updates <owner/repo>
                                Ask a GitHub release feed once at start-up whether a newer build
                                exists, and print the answer. Nothing is downloaded or applied.
                                Env: {EnvironmentVariables.CheckUpdates}
          -h, --help            Show this text.

        With no --serial and no --simulate the host serves an empty timeline. That is the honest
        result of having no data source, and it is never filled in with synthetic frames.

        Endpoints (all under http://localhost:<port>):
          /ws               WebSocket telemetry stream
          /stream           Server-Sent Events telemetry stream
          /api/status       Server state and the endpoint list it advertises
          /api/dvr/replay   Recorded timeline window (?t= seconds, ?window= seconds)
          /api/dvr/report   Incident report over the recorded window

        """;
}
