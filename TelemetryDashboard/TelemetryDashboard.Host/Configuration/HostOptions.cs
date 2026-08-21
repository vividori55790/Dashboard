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
    /// Directory of installed extensions, or null for <c>extensions/</c> beside the executable.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="PluginDirectory"/>, which is "load every DLL in here" and cannot
    /// express an extension that is installed but switched off. This directory is managed by the
    /// <c>extensions</c> subcommand and carries the enable/disable state alongside the files.
    /// </remarks>
    public string? ExtensionDirectory { get; init; }

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

    /// <summary>Server-Sent Events endpoint to ingest from, or null for none.</summary>
    /// <remarks>
    /// A hub that can only read a serial cable is a hub tied to one building. Any SSE feed becomes
    /// a source on equal terms: same routing, same scoring, same archive.
    /// </remarks>
    public string? SseEndpoint { get; init; }

    /// <summary>
    /// Id of the monitoring profile the simulator should produce, or null for the default.
    /// </summary>
    /// <remarks>
    /// Only meaningful with <see cref="Simulate"/>. Naming a profile that no file declares is an
    /// error rather than a fallback: silently running a different machine's channels than the one
    /// asked for is the defect profiles exist to remove.
    /// </remarks>
    public string? ProfileId { get; init; }

    /// <summary>A recorded CSV to play back instead of reading a live source, or null.</summary>
    /// <remarks>
    /// Everything downstream behaves exactly as it does live, because from its side nothing is
    /// different: the frames go through the parser, the routing rules and the analytics engine.
    /// What differs is that every frame says <c>REPLAY</c>, so a console cannot show last week's
    /// incident as though it were happening now.
    /// </remarks>
    public string? ReplayPath { get; init; }

    /// <summary>How fast to play a recording back. 1 is real time.</summary>
    public double ReplaySpeed { get; init; } = DefaultReplaySpeed;

    public const double DefaultReplaySpeed = 1.0;

    /// <summary>Whether an anomaly may transmit a command back to the device. Off unless asked.</summary>
    /// <remarks>
    /// This is the one flag in the host that makes the program <em>act on</em> the machine rather
    /// than watch it, so it is opt-in and it is refused without <see cref="SerialPort"/>: the only
    /// port it can write to is the one the operator already told it to open. A monitoring tool that
    /// transmits to hardware because a statistic crossed a threshold, without being asked to, is
    /// not a feature — and the controller behind it ships a default rule that auto-executes against
    /// a port called COM3, which is exactly the accident this refuses to have.
    /// </remarks>
    public bool EmergencyStop { get; init; }

    /// <summary>Sigma at which the interlock fires. Only meaningful with <see cref="EmergencyStop"/>.</summary>
    public double EmergencySigma { get; init; } = DefaultEmergencySigma;

    /// <summary>Command transmitted when the interlock fires.</summary>
    public string EmergencyCommand { get; init; } = DefaultEmergencyCommand;

    /// <summary>Seconds before the same channel may fire the interlock again.</summary>
    public double EmergencyCooldownSec { get; init; } = DefaultEmergencyCooldownSec;

    public const double DefaultEmergencySigma = 3.5;
    public const string DefaultEmergencyCommand = "$CMD,SAFE_MODE";
    public const double DefaultEmergencyCooldownSec = 5.0;

    /// <summary>Path to write a standalone HTML dashboard to, or null for none.</summary>
    /// <remarks>
    /// The file is a complete console for whatever profile is in force: one card and one trend per
    /// declared channel, in that channel's own unit and range. It connects back to this host's
    /// WebSocket, so it is a page to open on another machine rather than a screenshot.
    /// <para>
    /// The exporter has existed since M2 and was marked Built. Nothing constructed it, so the
    /// feature could not be reached from any running program — and once it could, two faults in
    /// the page it produced turned out never to have been exercised: the connection chip was the
    /// literal text "WS CONNECTED", updated by nothing, and a widget whose field was missing from
    /// a packet fell back to the temperature and then to zero.
    /// </para>
    /// </remarks>
    public string? DashboardExportPath { get; init; }

    /// <summary>HTTP endpoint to poll, or null for none.</summary>
    /// <remarks>
    /// Most public real-time data is request/response rather than a stream — open-data portals,
    /// USGS, exchange REST APIs, most industrial gateways. A hub that only reads streams cannot
    /// read any of them.
    /// </remarks>
    public string? PollEndpoint { get; init; }

    /// <summary>How often <see cref="PollEndpoint"/> is asked.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Channel map projecting JSON documents onto channels, or null for none.</summary>
    public string? ChannelMapPath { get; init; }

    /// <summary>Whether the operator asked for usage text.</summary>
    public bool ShowHelp { get; init; }

    /// <summary>Why the configuration was rejected, or null when it is usable.</summary>
    public string? Error { get; init; }
}
