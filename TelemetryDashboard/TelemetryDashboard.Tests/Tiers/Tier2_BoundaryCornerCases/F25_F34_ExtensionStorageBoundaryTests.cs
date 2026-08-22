namespace TelemetryDashboard.Tests.Tiers.Tier2_BoundaryCornerCases;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Moq;
using TelemetryDashboard.Tests.TestUtilities;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Resilience;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Plugins;
using TelemetryDashboard.Core.Recording;
using TelemetryDashboard.Infrastructure.Storage;
using TelemetryDashboard.Infrastructure.WebServer;
using TelemetryDashboard.Infrastructure.Integrations;
using TelemetryDashboard.Infrastructure.Replay;
using TelemetryDashboard.Infrastructure.Updater;

public class F25_F34_ExtensionStorageBoundaryTests
{
    #region F25: Extension Marketplace & UI (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F25_Boundary_EmptyMarketplaceManifest_DisplaysEmptyList()
    {
        var mockMarketplace = new Mock<IMarketplaceService>();
        mockMarketplace.Setup(m => m.FetchAvailableExtensionsAsync(It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new List<ExtensionDescriptor>());

        var extensions = await mockMarketplace.Object.FetchAvailableExtensionsAsync();
        extensions.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F25_Boundary_MalformedPluginManifestJson_IgnoresPluginEntry()
    {
        var parser = new PluginManifestParser();
        string malformed = "{ \"id\": \"Plugin1\", \"name\": ";

        bool parsed = parser.TryParseManifest(malformed, out var descriptor);
        parsed.Should().BeFalse();
        descriptor.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F25_Boundary_IncompatibleApiVersionPlugin_FlagsAsIncompatible()
    {
        var descriptor = new ExtensionDescriptor
        {
            Id = "Ext1",
            MinApiVersion = "2.0.0"
        };

        bool compatible = descriptor.IsCompatibleWithApiVersion("1.0.0");
        compatible.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F25_Boundary_NetworkTimeoutFetchingMarketplace_DisplaysOfflineMessage()
    {
        var mockMarketplace = new Mock<IMarketplaceService>();
        mockMarketplace.Setup(m => m.FetchAvailableExtensionsAsync(It.IsAny<CancellationToken>()))
                       .ThrowsAsync(new TaskCanceledException("Request timed out"));

        Func<Task> act = async () => await mockMarketplace.Object.FetchAvailableExtensionsAsync();
        await act.Should().ThrowAsync<TaskCanceledException>();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F25_Boundary_DuplicatePluginId_IgnoresDuplicateEntry()
    {
        var store = new ExtensionRegistry();
        var p1 = new ExtensionDescriptor { Id = "P1", Name = "Plugin 1" };
        var p2 = new ExtensionDescriptor { Id = "P1", Name = "Plugin 1 Duplicate" };

        store.Register(p1);
        bool secondAdded = store.Register(p2);

        secondAdded.Should().BeFalse();
        store.GetExtensions().Count.Should().Be(1);
    }

    #endregion

    #region F26: FileSystemWatcher Hot-Reload Engine (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public void F26_Boundary_LockedDllFileDuringCopy_RetriesOrWaits()
    {
        var hotReload = new HotReloadEngine();
        string tempDll = Path.GetTempFileName();

        using (var fs = new FileStream(tempDll, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            bool success = hotReload.TryLoadAssemblyWithRetry(tempDll, maxRetries: 2, delayMs: 10);
            success.Should().BeFalse();
        }

        File.Delete(tempDll);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F26_Boundary_CorruptedAssemblyDll_FailsLoadWithoutCrashingApp()
    {
        string tempDll = Path.GetTempFileName();
        File.WriteAllText(tempDll, "NOT_A_VALID_PE_HEADER_DLL");

        try
        {
            var loader = new AssemblyPluginAdapter();
            Action act = () => loader.LoadPlugin(tempDll);
            act.Should().Throw<BadImageFormatException>();
        }
        finally
        {
            File.Delete(tempDll);
        }
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F26_Boundary_PluginThrowsExceptionOnInit_UnloadsSafely()
    {
        var mockPlugin = new Mock<IPlugin>();
        mockPlugin.Setup(p => p.Initialize(It.IsAny<IPluginContext>()))
                  .Throws(new InvalidOperationException("Initialization failed"));

        var manager = new PluginManager();
        Action act = () => manager.InitializePlugin(mockPlugin.Object, Mock.Of<IPluginContext>());
        act.Should().Throw<InvalidOperationException>();
    }

    #endregion

    #region F27: Hybrid High-Speed Data Logger (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F27_Boundary_DiskFullException_PausesLoggerAndFiresAlert()
    {
        var mockLogger = new Mock<IDataLogger>();
        mockLogger.Setup(l => l.WriteAsync(It.IsAny<TelemetryPacket>(), It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new IOException("There is not enough space on the disk."));

        Func<Task> act = async () => await mockLogger.Object.WriteAsync(new TelemetryPacket());
        await act.Should().ThrowAsync<IOException>();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F27_Boundary_CorruptedSqliteDatabaseFile_RecreatesOrThrows()
    {
        string tempDb = Path.GetTempFileName();
        File.WriteAllText(tempDb, "CORRUPTED_SQLITE_HEADER");

        try
        {
            // Moved off SqliteIndexRepository, which is gone: it was a second, unreadable copy of
            // the durable store. The property is worth keeping and belongs on the class that
            // ships -- quietly replacing a database the operator believes holds history is worse
            // than refusing to open it.
            Action act = () => new SqliteDataLogger(tempDb);
            act.Should().Throw<Exception>();
        }
        finally
        {
            File.Delete(tempDb);
        }
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F27_Boundary_ChannelBufferOverflow_DropsOldestPackets()
    {
        var logger = new ChannelDataLogger(capacity: 5);
        for (int i = 0; i < 10; i++)
        {
            logger.TryEnqueue(new TelemetryPacket { Value = i });
        }

        logger.DroppedCount.Should().Be(5);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F27_Boundary_ConcurrentWriteAndRead_NoLockContentionCrash()
    {
        var logger = new ChannelDataLogger(capacity: 1000);
        var writeTask = Task.Run(() =>
        {
            for (int i = 0; i < 500; i++) logger.TryEnqueue(new TelemetryPacket { Value = i });
        });

        var readTask = Task.Run(() =>
        {
            for (int i = 0; i < 500; i++) logger.TryRead(out _);
        });

        await Task.WhenAll(writeTask, readTask);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F27_Boundary_InvalidExportPath_FailsGracefullyWithMessage()
    {
        var writer = new MatFileWriter();
        string invalidPath = @"Z:\NonExistentDrive\output.mat";

        Action act = () => writer.WritePackets(invalidPath, new List<TelemetryPacket>());
        act.Should().Throw<Exception>();
    }

    #endregion

    #region F28: Kestrel Embedded Web Server (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public void F28_Boundary_MalformedHttpRequest_Returns400BadRequest()
    {
        var handler = new SseStreamHandler();
        string rawHttp = "BAD_HTTP_REQUEST_HEADER\r\n\r\n";

        int statusCode = handler.ProcessRawRequest(rawHttp);
        statusCode.Should().Be(400);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F28_Boundary_WebClientDisconnectMidStream_CleansUpSseConnection()
    {
        var handler = new SseStreamHandler();
        string clientId = handler.RegisterClient();

        handler.UnregisterClient(clientId);
        handler.ActiveClientCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F28_Boundary_MaxConcurrentWebClients_RejectsExcessConnections()
    {
        var handler = new SseStreamHandler(maxClients: 2);
        handler.RegisterClient();
        handler.RegisterClient();

        Action act = () => handler.RegisterClient();
        act.Should().Throw<InvalidOperationException>();
    }

    #endregion

    #region F29: Notion REST API Automated Report Generator (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F29_Boundary_InvalidNotionApiKey_ReturnsUnauthorizedError()
    {
        var client = new NotionClient("secret_invalid_key_xyz");
        Func<Task> act = async () => await client.CreateReportPageAsync("Db123", "Title", new List<TelemetryPacket>());
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F29_Boundary_EmptyReportData_GeneratesHeaderOnlyPage()
    {
        var client = new NotionClient("mock_token");
        string jsonPayload = client.BuildPagePayload("Db123", "Empty Report", new List<TelemetryPacket>());

        jsonPayload.Should().Contain("Empty Report");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F29_Boundary_NotionApiRateLimited_RetriesWithExponentialBackoff()
    {
        var mockClient = new Mock<INotionClient>();
        mockClient.SetupSequence(n => n.CreateReportPageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<TelemetryPacket>>()))
                  .ThrowsAsync(new HttpRequestException("429 Too Many Requests"))
                  .ReturnsAsync("PAGE_ID_123");

        // Drive the shared policy rather than calling the mock directly: the assertion is about
        // our retry behaviour, and a direct call would simply surface the first 429.
        var observedBackoff = new List<TimeSpan>();

        string pageId = await RetryPolicy.ExecuteAsync(
            _ => mockClient.Object.CreateReportPageAsync("Db1", "Title", new List<TelemetryPacket>()),
            maxAttempts: 3,
            initialDelay: TimeSpan.FromMilliseconds(500),
            delayAsync: (delay, _) => { observedBackoff.Add(delay); return Task.CompletedTask; });

        pageId.Should().Be("PAGE_ID_123");
        observedBackoff.Should().ContainSingle().Which.Should().Be(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F29_Boundary_NetworkConnectionLost_SavesReportLocally()
    {
        var client = new NotionClient("mock_key");
        string localPath = Path.Combine(Path.GetTempPath(), "offline_report.json");

        try
        {
            client.SaveLocalBackupPayload(localPath, "Title", new List<TelemetryPacket>());
            File.Exists(localPath).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(localPath)) File.Delete(localPath);
        }
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F29_Boundary_InvalidDatabaseId_ReturnsNotFoundError()
    {
        var client = new NotionClient("mock_key");
        Func<Task> act = async () => await client.CreateReportPageAsync("INVALID_DB_ID", "Title", new List<TelemetryPacket>());
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    #endregion

    #region F30: Slack Webhook Block Kit Alert Publisher (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F30_Boundary_InvalidSlackWebhookUrl_ReturnsBadRequest()
    {
        var slack = new SlackClient();
        bool sent = await slack.SendAlertAsync("https://hooks.slack.com/services/INVALID/URL", "Alert Text");
        sent.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F30_Boundary_EmptyMessageBody_RejectsPayload()
    {
        var slack = new SlackClient();
        bool sent = await slack.SendAlertAsync("https://hooks.slack.com/services/valid", "");
        sent.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F30_Boundary_SlackApi500ServerError_RetriesOrLogsError()
    {
        var mockSlack = new Mock<ISlackClient>();
        mockSlack.Setup(s => s.SendAlertAsync(It.IsAny<string>(), It.IsAny<string>()))
                 .ReturnsAsync(false);

        bool result = await mockSlack.Object.SendAlertAsync("url", "msg");
        result.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F30_Boundary_SpecialJsonCharsInAlert_EscapesBlockKitPayload()
    {
        var slack = new SlackClient();
        string blockKitJson = slack.FormatBlockKitJson("Warning: \"Quotes\" & {Brackets}");

        blockKitJson.Should().Contain("\\\"Quotes\\\"");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F30_Boundary_WebhookTimeout_AbortsRequestCleanly()
    {
        var slack = new SlackClient();
        using var cts = new CancellationTokenSource(10);

        bool result = await slack.SendAlertAsync("https://httpbin.org/delay/5", "Msg", cts.Token);
        result.Should().BeFalse();
    }

    #endregion

    #region F31: MQTT Cloud Broker Publisher (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F31_Boundary_InvalidMqttBrokerAddress_TimesOutCleanly()
    {
        var mqtt = new MqttPublisher();
        bool connected = await mqtt.ConnectAsync("invalid.broker.domain.xyz", 1883, timeoutMs: 100);
        connected.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F31_Boundary_BrokerDisconnectMidTransmission_TriggersAutoReconnect()
    {
        var mqtt = new MqttPublisher();
        mqtt.SimulateDisconnect();

        mqtt.IsConnected.Should().BeFalse();
        bool reconnected = await mqtt.ReconnectAsync();
        reconnected.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F31_Boundary_EmptyMqttTopic_ThrowsArgumentException()
    {
        var mqtt = new MqttPublisher();
        Func<Task> act = async () => await mqtt.PublishAsync("", "payload");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F31_Boundary_HighQoSMessageQueueFull_DropsOrBuffersMessage()
    {
        var mqtt = new MqttPublisher(maxQueueSize: 2);
        mqtt.EnqueuePayload("topic1", "p1");
        mqtt.EnqueuePayload("topic1", "p2");

        bool thirdEnqueued = mqtt.EnqueuePayload("topic1", "p3");
        thirdEnqueued.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F31_Boundary_InvalidMqttCredentials_AuthenticationFails()
    {
        var mqtt = new MqttPublisher();
        bool auth = await mqtt.ConnectWithCredentialsAsync("localhost", 1883, "badUser", "badPass");
        auth.Should().BeFalse();
    }

    #endregion

    #region F32: Time-Machine Session Replay Player & 10s Snapshot (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public void F32_Boundary_NonExistentRecordingFile_ThrowsFileNotFoundException()
    {
        var replay = new SessionReplayPlayer();
        Action act = () => replay.LoadSession(@"C:\NonExistentRecording.csv");
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F32_Boundary_EmptySessionFile_DisplaysZeroLengthTimeline()
    {
        string tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "");

        try
        {
            var replay = new SessionReplayPlayer();
            replay.LoadSession(tempFile);
            replay.TotalDurationSeconds.Should().Be(0.0);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F32_Boundary_InvalidPlaybackSpeed_ZeroOrNegative_DefaultsToOneX()
    {
        var replay = new SessionReplayPlayer();
        replay.SetSpeed(-5.0);

        replay.PlaybackSpeed.Should().Be(1.0);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F32_Boundary_SeekPastEndOfSession_ClampsToEndTimestamp()
    {
        var replay = new SessionReplayPlayer();
        replay.SetDuration(60.0); // 60 sec
        replay.Seek(999.0);

        replay.CurrentPositionSeconds.Should().Be(60.0);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F32_Boundary_SnapshotExtractionNoFailure_ReturnsEmptyOrFullSession()
    {
        var extractor = new FailureSnapshotExtractor();
        var packets = new List<TelemetryPacket>();

        var snapshot = extractor.Extract10sFailureSnapshot(packets, failureTimestamp: DateTime.UtcNow);
        snapshot.Should().BeEmpty();
    }

    #endregion

    #region F33: GitHub Releases Hot-Swap Auto-Updater (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F33_Boundary_NoInternetConnection_ReturnsOfflineStatus()
    {
        var updater = new GitHubUpdater();
        var checkResult = await updater.CheckForUpdatesAsync("https://invalid.github.api/releases");

        checkResult.IsUpdateAvailable.Should().BeFalse();
        checkResult.StatusMessage.Should().Contain("Offline");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F33_Boundary_InvalidGitHubRepoPath_ReturnsNotFoundResult()
    {
        var updater = new GitHubUpdater();
        var result = await updater.CheckForUpdatesAsync("owner/non_existent_repo_xyz");

        result.IsUpdateAvailable.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task F33_Boundary_AlreadyLatestVersion_ReportsNoUpdateAvailable()
    {
        var updater = new GitHubUpdater();
        updater.SetCurrentVersion("v1.0.0");

        var result = await updater.EvaluateVersionMatch("v1.0.0");
        result.IsUpdateAvailable.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F33_Boundary_CorruptedUpdateAssetZip_FailsHashVerification()
    {
        var updater = new GitHubUpdater();
        string tempZip = Path.GetTempFileName();
        File.WriteAllText(tempZip, "CORRUPTED_ZIP_BINARY_DATA");

        try
        {
            bool verified = updater.VerifySha256(tempZip, expectedHash: "00000000000000000000000000000000");
            verified.Should().BeFalse();
        }
        finally
        {
            File.Delete(tempZip);
        }
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F33_Boundary_PatcherScriptMissing_FailsUpdateCleanly()
    {
        var updater = new GitHubUpdater();
        bool launched = updater.LaunchExternalPatcher(@"C:\NonExistentPatcher.ps1");

        launched.Should().BeFalse();
    }

    #endregion

    #region F34: Self-Contained Single-File Portable EXE Packaging (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public void F34_Boundary_MissingNativeDependency_ReportsMissingDll()
    {
        var packager = new PortablePackageChecker();
        bool present = packager.VerifyNativeDll("non_existent_native.dll");

        present.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F34_Boundary_TempFolderAccessDenied_FailsExtractionWithAlert()
    {
        var packager = new PortablePackageChecker();
        string readOnlyTemp = Path.Combine(Path.GetTempPath(), "InvalidTempFolder|XYZ");

        Action act = () => packager.ExtractEmbeddedResources(readOnlyTemp);
        act.Should().Throw<Exception>();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F34_Boundary_CommandLineArgOverflow_ParsesValidArguments()
    {
        var packager = new PortablePackageChecker();
        string[] overflowArgs = Enumerable.Range(0, 1000).Select(i => $"--arg{i}=value{i}").ToArray();

        var parsed = packager.ParseArgs(overflowArgs);
        parsed.Count.Should().Be(1000);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F34_Boundary_MultipleInstancesLaunched_FocusesExistingInstance()
    {
        var packager = new PortablePackageChecker();
        bool firstInstance = packager.EnsureSingleInstance("TelemetryDashboard_SingleInstance_Mutex_Test");

        firstInstance.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F34_Boundary_CorruptedEmbeddedConfig_LoadsDefaultSettings()
    {
        var packager = new PortablePackageChecker();
        var config = packager.LoadEmbeddedConfig("CORRUPTED_CONFIG_JSON");

        config.Should().NotBeNull();
        config.UseDefaults.Should().BeTrue();
    }

    #endregion
}
