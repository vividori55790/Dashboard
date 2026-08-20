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
        PollEndpoint = defaults.PollEndpoint;
        PollInterval = defaults.PollInterval;
    }

    public int Port;
    public int BaudRate;
    public List<string> ContentRoots;
    public string? ClientFile;
    public string? SerialPort;
    public string? RecordingDirectory;
    public bool Simulate;
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
        PollEndpoint = PollEndpoint,
        PollInterval = PollInterval
    };
}
