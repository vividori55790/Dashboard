using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Host.Configuration;

/// <summary>
/// Everything the headless host must know before it binds a socket or opens a port.
/// </summary>
/// <remarks>
/// Immutable and validated once, at startup, so the banner can describe the run without asking
/// live objects what they were configured with. <see cref="Error"/> travels with the options
/// instead of being thrown: a bad command line is an expected outcome that deserves a message and
/// an exit code, not a stack trace.
/// </remarks>
public sealed class HostOptions
{
    /// <summary>Port used when neither the command line nor the environment names one.</summary>
    public const int DefaultPort = 8080;

    /// <summary>Baud rate used when the command line names a serial port but no speed.</summary>
    public const int DefaultBaudRate = 115200;

    /// <summary>Broker port used when an address names no port.</summary>
    public const int DefaultMqttPort = 1883;

    /// <summary>Topic prefix used when none is configured.</summary>
    public const string DefaultMqttTopicPrefix = "telemetry";

    /// <summary>TCP port the streaming server is asked to bind.</summary>
    public int Port { get; init; } = DefaultPort;

    /// <summary>Directories whose files may be served, in search order.</summary>
    /// <remarks>
    /// Empty means "the directory the executable lives in", resolved at start rather than here so
    /// this type stays a plain description of what the operator asked for.
    /// </remarks>
    public IReadOnlyList<string> ContentRoots { get; init; } = Array.Empty<string>();

    /// <summary>HTML file served at <c>/</c>, or null to let the host look for a known console.</summary>
    public string? ClientFile { get; init; }

    /// <summary>Serial port to open, or null when no hardware is attached.</summary>
    public string? SerialPort { get; init; }

    /// <summary>Speed of <see cref="SerialPort"/>.</summary>
    public int BaudRate { get; init; } = DefaultBaudRate;

    /// <summary>Directory for the CSV recording, or null to record nothing.</summary>
    public string? RecordingDirectory { get; init; }

    /// <summary>
    /// Whether to run the virtual simulator instead of hardware.
    /// </summary>
    /// <remarks>
    /// Opt-in by an explicit flag, never a fallback. A hub that quietly substitutes synthetic data
    /// when no device answers produces a dashboard that is indistinguishable from a working one,
    /// which is the single failure this codebase refuses to allow.
    /// </remarks>
    public bool Simulate { get; init; }

    /// <summary>
    /// Directory scanned for plugin assemblies, or null for <c>plugins/</c> beside the executable.
    /// </summary>
    /// <remarks>
    /// Defaulted rather than opt-in because the build already stages the sample plugin into that
    /// directory: a host that ignored it would leave the extension surface exactly as inert as it
    /// was before it had one.
    /// </remarks>
    public string? PluginDirectory { get; init; }

    /// <summary>
    /// URL or path of the extension catalogue index, or null to consult no catalogue.
    /// </summary>
    /// <remarks>
    /// Listing only. Installing an extension means running a third party's code in this process,
    /// which is a decision an operator makes explicitly and not a side effect of naming a
    /// catalogue.
    /// </remarks>
    public string? ExtensionCatalogue { get; init; }

    /// <summary>Slack incoming webhook for anomaly alerts, or null to alert nobody.</summary>
    /// <remarks>
    /// Opt-in. A host that posted to a workspace because a default pointed somewhere would be
    /// sending messages on an operator's behalf that they never asked for.
    /// </remarks>
    public string? SlackWebhook { get; init; }

    /// <summary>MQTT broker to republish telemetry to, or null to publish nowhere.</summary>
    public string? MqttBrokerHost { get; init; }

    /// <summary>Port of <see cref="MqttBrokerHost"/>.</summary>
    public int MqttBrokerPort { get; init; } = DefaultMqttPort;

    /// <summary>Topic prefix each channel is published beneath.</summary>
    public string MqttTopicPrefix { get; init; } = DefaultMqttTopicPrefix;

    /// <summary>
    /// <c>owner/repo</c> or a releases URL to check for a newer build, or null to check nothing.
    /// </summary>
    /// <remarks>
    /// Checking only. The host reports what it found and applies nothing: an update channel that
    /// installs on its own is a remote code execution path into a plant network.
    /// </remarks>
    public string? UpdateRepository { get; init; }

    /// <summary>Whether the operator asked for usage text.</summary>
    public bool ShowHelp { get; init; }

    /// <summary>Why the configuration was rejected, or null when it is usable.</summary>
    public string? Error { get; init; }
}
