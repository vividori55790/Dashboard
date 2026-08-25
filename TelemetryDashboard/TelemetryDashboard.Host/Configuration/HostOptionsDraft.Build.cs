namespace TelemetryDashboard.Host.Configuration;

/// <summary>
/// Turning the accumulated flags into the immutable options the run is given.
/// </summary>
/// <remarks>
/// Split from the field list when --credential took the file one line past the 150-line rule.
/// Splitting here rather than exempting because the two halves answer different questions: above is
/// what a command line may say, and this is what the host is handed once it has finished saying it.
/// </remarks>
internal sealed partial class HostOptionsDraft
{
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
        RetentionSpec = RetentionSpec,
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
        PollInterval = PollInterval,
        ExpectedNodes = ExpectedNodes,
        RetiredNodes = RetiredNodes,
        CoverageStatePath = CoverageStatePath,
        RulesPath = RulesPath,
        CredentialPath = CredentialPath,
        ListenOnAllInterfaces = ListenOnAllInterfaces
    };
}
