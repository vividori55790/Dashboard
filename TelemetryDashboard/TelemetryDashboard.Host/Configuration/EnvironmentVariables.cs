using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace TelemetryDashboard.Host.Configuration;

/// <summary>
/// Reads the host's configuration out of the process environment.
/// </summary>
/// <remarks>
/// Environment first, command line second: a container image or a systemd unit sets the
/// environment once, and an operator debugging that same image overrides one value on the command
/// line without rewriting the unit file.
/// </remarks>
public static class EnvironmentVariables
{
    /// <summary>Listen port. Same meaning as <c>--port</c>.</summary>
    public const string Port = "TELEMETRY_HOST_PORT";

    /// <summary>Static content directories, separated by the platform path separator.</summary>
    public const string WebRoot = "TELEMETRY_HOST_WEB_ROOT";

    /// <summary>HTML file served at <c>/</c>. Same meaning as <c>--client</c>.</summary>
    public const string Client = "TELEMETRY_HOST_CLIENT";

    /// <summary>Serial port to open. Same meaning as <c>--serial</c>.</summary>
    public const string Serial = "TELEMETRY_HOST_SERIAL";

    /// <summary>Serial speed. Same meaning as <c>--baud</c>.</summary>
    public const string Baud = "TELEMETRY_HOST_BAUD";

    /// <summary>CSV recording directory. Same meaning as <c>--record</c>.</summary>
    public const string Record = "TELEMETRY_HOST_RECORD";

    /// <summary>Set to <c>1</c>, <c>true</c> or <c>yes</c> to run the simulator.</summary>
    public const string Simulate = "TELEMETRY_HOST_SIMULATE";

    /// <summary>Derive a per-channel interval channel. See <see cref="HostOptions.WatchIntervals"/>.</summary>
    public const string WatchIntervals = "TELEMETRY_HOST_WATCH_INTERVALS";

    /// <summary>Plugin discovery directory. Same meaning as <c>--plugin-dir</c>.</summary>
    public const string PluginDir = "TELEMETRY_HOST_PLUGIN_DIR";

    /// <summary>Extension catalogue index. Same meaning as <c>--extensions</c>.</summary>
    public const string Extensions = "TELEMETRY_HOST_EXTENSIONS";

    /// <summary>Slack incoming webhook. Same meaning as <c>--slack-webhook</c>.</summary>
    public const string SlackWebhook = "TELEMETRY_HOST_SLACK_WEBHOOK";

    /// <summary>MQTT broker as <c>host</c> or <c>host:port</c>. Same meaning as <c>--mqtt</c>.</summary>
    public const string MqttBroker = "TELEMETRY_HOST_MQTT";

    /// <summary>MQTT topic prefix. Same meaning as <c>--mqtt-topic</c>.</summary>
    public const string MqttTopic = "TELEMETRY_HOST_MQTT_TOPIC";

    /// <summary>Release feed to check. Same meaning as <c>--check-updates</c>.</summary>
    public const string CheckUpdates = "TELEMETRY_HOST_CHECK_UPDATES";

    /// <summary>Builds the starting options the command line then overrides.</summary>
    /// <remarks>
    /// A malformed value is reported rather than ignored. Silently falling back to 8080 because
    /// <c>TELEMETRY_HOST_PORT=eighty-eighty</c> did not parse would leave the operator looking for
    /// a listener on a port nobody asked for.
    /// </remarks>
    public static HostOptions Read()
    {
        string? rawPort = Value(Port);
        string? rawBaud = Value(Baud);

        if (rawPort is not null && !TryPort(rawPort, out _))
        {
            return new HostOptions { Error = $"{Port}='{rawPort}' is not a TCP port between 1 and 65535." };
        }

        if (rawBaud is not null && !TryBaud(rawBaud, out _))
        {
            return new HostOptions { Error = $"{Baud}='{rawBaud}' is not a positive baud rate." };
        }

        string? rawBroker = Value(MqttBroker);
        if (rawBroker is not null
            && !ArgumentCursor.TryHostAndPort(rawBroker, HostOptions.DefaultMqttPort, out _, out _))
        {
            return new HostOptions { Error = $"{MqttBroker}='{rawBroker}' is not a broker address; expected host or host:port." };
        }

        TryPort(rawPort ?? string.Empty, out int port);
        TryBaud(rawBaud ?? string.Empty, out int baud);
        ArgumentCursor.TryHostAndPort(rawBroker ?? string.Empty, HostOptions.DefaultMqttPort, out string brokerHost, out int brokerPort);

        return new HostOptions
        {
            Port = rawPort is null ? HostOptions.DefaultPort : port,
            BaudRate = rawBaud is null ? HostOptions.DefaultBaudRate : baud,
            ContentRoots = SplitRoots(Value(WebRoot)),
            ClientFile = Value(Client),
            SerialPort = Value(Serial),
            RecordingDirectory = Value(Record),
            Simulate = IsTruthy(Value(Simulate)),
            WatchIntervals = IsTruthy(Value(WatchIntervals)),
            PluginDirectory = Value(PluginDir),
            ExtensionCatalogue = Value(Extensions),
            SlackWebhook = Value(SlackWebhook),
            MqttBrokerHost = rawBroker is null ? null : brokerHost,
            MqttBrokerPort = brokerPort,
            MqttTopicPrefix = Value(MqttTopic) ?? HostOptions.DefaultMqttTopicPrefix,
            UpdateRepository = Value(CheckUpdates)
        };
    }

    /// <summary>Parses a TCP port, rejecting 0 and anything outside the 16-bit range.</summary>
    public static bool TryPort(string raw, out int port) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) && port is > 0 and <= 65535;

    /// <summary>Parses a baud rate, rejecting zero and negatives.</summary>
    public static bool TryBaud(string raw, out int baud) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out baud) && baud > 0;

    private static string? Value(string name)
    {
        string? raw = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    private static IReadOnlyList<string> SplitRoots(string? raw) =>
        raw is null
            ? Array.Empty<string>()
            : raw.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsTruthy(string? raw) =>
        raw is not null &&
        (raw.Equals("1", StringComparison.Ordinal) ||
         raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
         raw.Equals("yes", StringComparison.OrdinalIgnoreCase));
}
