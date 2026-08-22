using System.Collections.Generic;

namespace TelemetryDashboard.Host.Configuration;

/// <summary>
/// The options as they are being assembled, before they are frozen into <see cref="HostOptions"/>.
/// </summary>
/// <remarks>
/// Exists so the parser is a readable list of what the host accepts rather than a wall of local
/// variables that has to be declared once, mutated once and copied once for every option. That
/// shape is what made adding an option cost three edits in three places, which is how an option
/// ends up half-wired. <see cref="HostOptions"/> stays immutable; this is the mutable staging area
/// that produces it.
/// </remarks>
internal sealed class HostOptionsDraft
{
    public HostOptionsDraft(HostOptions defaults)
    {
        Port = defaults.Port;
        BaudRate = defaults.BaudRate;
        ContentRoots = new List<string>(defaults.ContentRoots);
        ClientFile = defaults.ClientFile;
        SerialPort = defaults.SerialPort;
        RecordingDirectory = defaults.RecordingDirectory;
        Simulate = defaults.Simulate;
        WatchIntervals = defaults.WatchIntervals;
        MaxStreamClients = defaults.MaxStreamClients;
        DriftWindowSeconds = defaults.DriftWindowSeconds;
        IncidentDirectory = defaults.IncidentDirectory;
        PluginDirectory = defaults.PluginDirectory;
        ExtensionDirectory = defaults.ExtensionDirectory;
        ExtensionCatalogue = defaults.ExtensionCatalogue;
        SlackWebhook = defaults.SlackWebhook;
        MqttBrokerHost = defaults.MqttBrokerHost;
        MqttBrokerPort = defaults.MqttBrokerPort;
        MqttTopicPrefix = defaults.MqttTopicPrefix;
        UpdateRepository = defaults.UpdateRepository;
        SseEndpoint = defaults.SseEndpoint;
        ChannelMapPath = defaults.ChannelMapPath;
        ProfileId = defaults.ProfileId;
        DashboardExportPath = defaults.DashboardExportPath;
        ArchivePath = defaults.ArchivePath;
        Computed = new List<string>(defaults.Computed);
        Limits = new List<string>(defaults.Limits);
        EmergencyLimits = new List<string>(defaults.EmergencyLimits);
        Signals = new List<string>(defaults.Signals);
        ReplayPath = defaults.ReplayPath;
        ReplaySpeed = defaults.ReplaySpeed;
        EmergencyStop = defaults.EmergencyStop;
        EmergencySigma = defaults.EmergencySigma;
        EmergencyCommand = defaults.EmergencyCommand;
        EmergencyCooldownSec = defaults.EmergencyCooldownSec;
        PollEndpoint = defaults.PollEndpoint;
        PollInterval = defaults.PollInterval;
    }

    public int Port;
    public int BaudRate;
    public List<string> ContentRoots;
    public List<string> Computed;
    public List<string> Limits;
    public List<string> EmergencyLimits;
    public List<string> Signals;
    public string? ClientFile;
    public string? SerialPort;
    public string? RecordingDirectory;
    public bool Simulate;

    public bool WatchIntervals;

    public int MaxStreamClients;

    public int DriftWindowSeconds;

    public string? IncidentDirectory;
    public string? PluginDirectory;
    public string? ExtensionDirectory;
    public string? ExtensionCatalogue;
    public string? SlackWebhook;
    public string? MqttBrokerHost;
    public int MqttBrokerPort;
    public string MqttTopicPrefix;
    public string? UpdateRepository;
    public string? SseEndpoint;
    public string? ChannelMapPath;
    public string? ProfileId;
    public string? DashboardExportPath;
    public string? ArchivePath;
    public string? ReplayPath;
    public double ReplaySpeed = HostOptions.DefaultReplaySpeed;
    public bool EmergencyStop;
    public double EmergencySigma = HostOptions.DefaultEmergencySigma;
    public string EmergencyCommand = HostOptions.DefaultEmergencyCommand;
    public double EmergencyCooldownSec = HostOptions.DefaultEmergencyCooldownSec;
    public string? PollEndpoint;
    public TimeSpan PollInterval;

    public HostOptions Build() => new()
    {
        Port = Port,
        BaudRate = BaudRate,
        ContentRoots = ContentRoots,
        ClientFile = ClientFile,
        SerialPort = SerialPort,
        RecordingDirectory = RecordingDirectory,
        Simulate = Simulate,
        WatchIntervals = WatchIntervals,
        MaxStreamClients = MaxStreamClients,
        DriftWindowSeconds = DriftWindowSeconds,
        IncidentDirectory = IncidentDirectory,
        PluginDirectory = PluginDirectory,
        ExtensionDirectory = ExtensionDirectory,
        ExtensionCatalogue = ExtensionCatalogue,
        SlackWebhook = SlackWebhook,
        MqttBrokerHost = MqttBrokerHost,
        MqttBrokerPort = MqttBrokerPort,
        MqttTopicPrefix = MqttTopicPrefix,
        UpdateRepository = UpdateRepository,
        SseEndpoint = SseEndpoint,
        ChannelMapPath = ChannelMapPath,
        ProfileId = ProfileId,
        DashboardExportPath = DashboardExportPath,
        ArchivePath = ArchivePath,
        Computed = Computed,
        Limits = Limits,
        EmergencyLimits = EmergencyLimits,
        Signals = Signals,
        ReplayPath = ReplayPath,
        ReplaySpeed = ReplaySpeed,
        EmergencyStop = EmergencyStop,
        EmergencySigma = EmergencySigma,
        EmergencyCommand = EmergencyCommand,
        EmergencyCooldownSec = EmergencyCooldownSec,
        PollEndpoint = PollEndpoint,
        PollInterval = PollInterval
    };
}
