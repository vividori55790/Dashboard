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
    /// Whether every channel also gets a <c>.interval</c> channel of seconds since it last reported.
    /// </summary>
    /// <remarks>
    /// Off by default because it roughly doubles the record count. On, because a dead sensor is
    /// otherwise indistinguishable from a steady one: every chart here draws the last value it was
    /// given, so a link that drops holds its final reading on screen, inside its limits, with a
    /// z-score of zero. The absence of values is the whole failure, and no value-watching alarm can
    /// see it.
    /// </remarks>
    public bool WatchIntervals { get; init; }

    /// <summary>
    /// Most concurrent stream subscribers, or 0 for the default.
    /// </summary>
    /// <remarks>
    /// Every subscriber is a long-lived connection and every frame is fanned out to all of them, so
    /// the cost of one more is paid by the ones already being served. There was no ceiling at all
    /// until this existed.
    /// </remarks>
    public int MaxStreamClients { get; init; }

    /// <summary>
    /// Whether every channel also gets a <c>.drift</c> channel, or 0 for off.
    /// </summary>
    /// <remarks>
    /// The number is the long memory in seconds: how far back "where this channel has been living"
    /// reaches. It is the only detector here that can see a fault which never trips a threshold --
    /// a z-score measures a reading against the window it just came from, so anything slow enough
    /// drags its own baseline along and never scores.
    /// </remarks>
    public int DriftWindowSeconds { get; init; }

    /// <summary>
    /// Directory to write an incident report into when a limit is crossed, or null for none.
    /// </summary>
    /// <remarks>
    /// Needs an archive: the report is the window before the crossing, and that comes out of it.
    /// The one moment in a run that unambiguously means "capture what led to this" is the crossing
    /// itself, and until now nothing acted on it -- the report existed only if somebody asked
    /// /api/incident with the right timestamp, which nobody does at three in the morning.
    /// </remarks>
    public string? IncidentDirectory { get; init; }

    /// <summary>
    /// Retention policy for the archive, or null to keep everything forever.
    /// </summary>
    /// <remarks>
    /// Asking for one changes the archive's layout as well as its lifetime: it becomes the tiered
    /// store, which keeps compressed blocks and rollups instead of a row per sample, and which is
    /// the only layout here that can be pruned. The row store keeps the original wire text and this
    /// one does not, so it is a choice about what the archive is for rather than a size knob.
    /// </remarks>
    public string? RetentionSpec { get; init; }

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

    /// <summary>SQLite file to archive every ingested sample into, or null for none.</summary>
    /// <remarks>
    /// Separate from <see cref="RecordingDirectory"/>, which writes CSV. A CSV is a transcript: to
    /// answer a question about one channel last Tuesday you re-parse the whole file. The archive is
    /// queryable by node, channel and time window, and <c>/api/history</c> serves it, so the
    /// cross-platform half of the product finally has somewhere to look back.
    /// </remarks>
    public string? ArchivePath { get; init; }

    /// <summary>Whether this run asked for a telemetry source at all.</summary>
    /// <remarks>
    /// A host with no source is a legitimate configuration — it serves the console and waits — but
    /// a host that <em>was</em> given one and could not open it is a failure, and the two used to be
    /// the same outcome for everything except serial. <c>--simulate --profile does-not-exist</c>
    /// printed the error and then ran with an empty timeline and exit code 0, which looks exactly
    /// like a rig that has not been plugged in yet.
    /// <para>
    /// Poll and SSE endpoints are absent here because constructing those sources cannot fail; they
    /// report a bad address once they try to reach it, which is the right moment for a network.
    /// </para>
    /// </remarks>
    public bool SourceRequested => SerialPort is not null || ReplayPath is not null || Simulate;

    /// <summary>Whether this run's telemetry is generated from a monitoring profile.</summary>
    /// <remarks>
    /// True for <c>--simulate</c> and for the loopback port, which generates the same frames and
    /// sends them through an in-memory device. Both need the profile's limits and derived channels;
    /// checking only <c>Simulate</c> left a loopback run with a serial path, an armed interlock and
    /// no limits for it to act on.
    /// </remarks>
    public bool GeneratesFromProfile =>
        Simulate || string.Equals(SerialPort, "loopback", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Derived channels declared on the command line, as <c>id[unit] = expression</c>.
    /// </summary>
    /// <remarks>
    /// Added to whatever the profile declares rather than replacing it, because the two answer
    /// different questions: the profile describes the rig, and these describe what this particular
    /// run wants to watch. A declaration here that repeats an id from the profile replaces that
    /// one, so an operator can override without editing the profile file.
    /// </remarks>
    public IReadOnlyList<string> Computed { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Engineering limits, as <c>channel[unit] in lo..hi</c> or a single comparison.
    /// </summary>
    /// <remarks>
    /// Separate from the profile's channel ranges on purpose, and the distinction matters: a
    /// profile's Minimum and Maximum bound what an operator may <em>set</em> on a slider, while
    /// these bound what the machine may safely <em>do</em>. They are frequently different numbers —
    /// a slider that reaches 450 V on a bus whose ceiling is 420 V is a deliberate way to inject a
    /// fault — and conflating them turns every test excursion into an alarm and every alarm into
    /// something the operator learns to ignore.
    /// </remarks>
    public IReadOnlyList<string> Limits { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Limits that additionally trip the emergency interlock, not merely raise an alarm.
    /// </summary>
    /// <remarks>
    /// A separate flag because they are separate authorisations. Every declared limit says
    /// "somebody should look"; these say "act on the machine", and the second is not a louder
    /// version of the first. Making every band excursion a trip would be its own kind of unsafe —
    /// a converter shut down for a two-sample overshoot is a converter whose interlock gets
    /// disabled by the end of the week.
    /// <para>
    /// These are also ordinary limits: they appear in <c>/api/limits</c>, raise the same alarm and
    /// carry the same unit check. The flag adds an action, it does not create a second rule set.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> EmergencyLimits { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Known waveforms to drive channels with, as <c>channel=shape@frequencyHz:amplitude</c>.
    /// </summary>
    /// <remarks>
    /// Only meaningful for a generated source, which is the only thing whose values this host
    /// decides. On a real rig the channel reads what the converter is doing, and a flag that
    /// claimed otherwise would be describing a machine it does not control.
    /// </remarks>
    public IReadOnlyList<string> Signals { get; init; } = Array.Empty<string>();

    /// <summary>A recorded CSV to play back instead of reading a live source, or null.</summary>
    /// <remarks>
    /// Everything downstream behaves exactly as it does live, because from its side nothing is
    /// different: the frames go through the parser, the routing rules and the analytics engine.
    /// What differs is that every frame says <c>REPLAY</c>, so a console cannot show last week's
    /// incident as though it were happening now.
    /// </remarks>
    public string? ReplayPath { get; init; }

    /// <summary>Nodes this rig has, whether or not any of them has ever been heard from.</summary>
    /// <remarks>
    /// The only way a node that never started can be reported as missing. The ledger learns the
    /// nodes that do speak, which catches the common failure — something that worked and stopped —
    /// and is blind to the one an operator is most likely to be commissioning around: a converter
    /// whose MCU has never come up at all. To a learning-only ledger that node does not exist.
    /// </remarks>
    public IReadOnlyList<string> ExpectedNodes { get; init; } = [];

    /// <summary>Nodes decommissioned on purpose, so they stop being reported as missing.</summary>
    /// <remarks>
    /// Needed the moment the learned set is remembered across restarts: without it a node that was
    /// removed from the rig is missing for ever, and an alarm that can never be cleared is one
    /// people learn to ignore. Applied after the state file is restored, so it wins over it.
    /// </remarks>
    public IReadOnlyList<string> RetiredNodes { get; init; } = [];

    /// <summary>
    /// File saying what the device on this bench actually sends, or null for the defaults.
    /// </summary>
    /// <remarks>
    /// The built-in rules recognise the framing this product's own generated firmware emits, which
    /// is the framing a real installation does not have. A bench STM32 names its own channels and
    /// may report in its own units, and without a rule saying so every band, computed channel and
    /// twin placement the profile declares matches nothing at all -- readings on screen with
    /// nothing judging them.
    /// </remarks>
    public string? RulesPath { get; init; }

    /// <summary>
    /// File holding the credential the console will demand, or null to serve its machine openly.
    /// </summary>
    /// <remarks>
    /// Null is the default and is today's behaviour: a console bound to loopback is reachable only
    /// by somebody already sitting at the machine, and asking them for a password is ceremony. The
    /// flag exists because the argument for ever binding this endpoint wider has always stopped at
    /// the same sentence -- it has no authentication -- and the lock has to work before there is
    /// any question of opening the door.
    /// </remarks>
    public string? CredentialPath { get; init; }

    /// <summary>
    /// True when the console should bind every interface instead of loopback only.
    /// </summary>
    /// <remarks>
    /// Cannot be set without <see cref="CredentialPath"/>; the parser refuses the pair and
    /// <c>TelemetryStreamingServer.Start</c> refuses it again at the socket. What neither can
    /// supply is confidentiality: HTTP Basic is base64, so on a cleartext link the password is
    /// readable by anything on the path.
    /// <para>
    /// The alternative considered was allowing this only behind a reverse proxy. It was rejected
    /// because a process cannot verify that a TLS terminator is in front of it -- an
    /// <c>X-Forwarded-Proto</c> header is written by whoever connects -- so the flag's safety
    /// would have rested entirely on a sentence in a document, which is the kind of claim this
    /// codebase has rules against. Proxying also needs no flag: it works today against the
    /// loopback binding, unchanged.
    /// </para>
    /// <para>
    /// So this is the other answer, stated plainly instead: the operator declares that the segment
    /// is theirs, and the banner and <c>/api/status</c> both say what is and is not protected.
    /// </para>
    /// </remarks>
    public bool ListenOnAllInterfaces { get; init; }

    /// <summary>File the learned node set is remembered in, or null to forget on every restart.</summary>
    /// <remarks>
    /// Opt-in because it writes a file the operator did not otherwise ask for. Worth asking for:
    /// without it a restart forgets that a node ever existed, and its absence becomes undetectable
    /// again at exactly the moment somebody restarts the hub to investigate why data is missing.
    /// </remarks>
    public string? CoverageStatePath { get; init; }

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
