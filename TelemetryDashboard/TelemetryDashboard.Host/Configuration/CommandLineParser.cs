using System.Globalization;
using System;
using System.Collections.Generic;
using System.IO;
using TelemetryDashboard.Host.Ingest;

namespace TelemetryDashboard.Host.Configuration;

/// <summary>
/// Turns command-line arguments into <see cref="HostOptions"/>, over the environment's defaults.
/// </summary>
/// <remarks>
/// Paths are validated here rather than at first use. A web root that does not exist is registered
/// by <c>StaticContentHost</c> as no root at all, so the console would answer 404 for every asset
/// while the host reported a clean start; the operator deserves to hear about the typo instead.
/// </remarks>
public static class CommandLineParser
{
    /// <summary>Applies <paramref name="args"/> on top of <paramref name="defaults"/>.</summary>
    public static HostOptions Parse(string[] args, HostOptions defaults)
    {
        if (defaults.Error is not null) return defaults;

        var draft = new HostOptionsDraft(defaults);

        for (int i = 0; i < args.Length; i++)
        {
            string argument = args[i];
            switch (argument)
            {
                case "--help" or "-h" or "-?" or "/?":
                    return new HostOptions { ShowHelp = true };

                case "--simulate":
                    draft.Simulate = true;
                    break;

                case "--watch-intervals":
                    draft.WatchIntervals = true;
                    break;

                case "--retain":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawRetain)) return ArgumentCursor.MissingValue(argument);
                    if (!Core.Storage.RetentionSpec.TryParse(rawRetain, out _, out string? retainError))
                    {
                        // Refused here rather than at the store, because this is the one setting in
                        // the product that destroys data and a run that started on a misread policy
                        // has already deleted something by the time anyone looks.
                        return ArgumentCursor.Fail($"--retain {rawRetain}: {retainError}");
                    }
                    draft.RetentionSpec = rawRetain;
                    break;

                case "--incident-dir":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawIncident)) return ArgumentCursor.MissingValue(argument);
                    draft.IncidentDirectory = System.IO.Path.GetFullPath(rawIncident);
                    break;

                case "--watch-drift":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawDrift)) return ArgumentCursor.MissingValue(argument);
                    if (!int.TryParse(rawDrift, out draft.DriftWindowSeconds) || draft.DriftWindowSeconds < 10)
                    {
                        return ArgumentCursor.Fail(
                            $"'{rawDrift}' is not a drift window in seconds. Below ten seconds the long "
                            + "average tracks the short one and their difference is noise, not drift.");
                    }
                    break;

                case "--max-clients":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawClients)) return ArgumentCursor.MissingValue(argument);
                    if (!int.TryParse(rawClients, out draft.MaxStreamClients) || draft.MaxStreamClients < 1)
                    {
                        return ArgumentCursor.Fail($"'{rawClients}' is not a client count of 1 or more.");
                    }
                    break;

                case "--port" or "-p":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawPort)) return ArgumentCursor.MissingValue(argument);
                    if (!EnvironmentVariables.TryPort(rawPort, out draft.Port)) return ArgumentCursor.Fail($"'{rawPort}' is not a TCP port between 1 and 65535.");
                    break;

                case "--baud" or "-b":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawBaud)) return ArgumentCursor.MissingValue(argument);
                    if (!EnvironmentVariables.TryBaud(rawBaud, out draft.BaudRate)) return ArgumentCursor.Fail($"'{rawBaud}' is not a positive baud rate.");
                    break;

                case "--web-root" or "-w":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawRoot)) return ArgumentCursor.MissingValue(argument);
                    if (!Directory.Exists(rawRoot)) return ArgumentCursor.Fail($"web root '{rawRoot}' does not exist.");
                    draft.ContentRoots.Add(Path.GetFullPath(rawRoot));
                    break;

                case "--client" or "-c":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawClient)) return ArgumentCursor.MissingValue(argument);
                    if (!File.Exists(rawClient)) return ArgumentCursor.Fail($"client file '{rawClient}' does not exist.");
                    draft.ClientFile = Path.GetFullPath(rawClient);
                    break;

                case "--serial" or "-s":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawSerial)) return ArgumentCursor.MissingValue(argument);
                    draft.SerialPort = rawSerial;
                    break;

                case "--record" or "-r":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawRecord)) return ArgumentCursor.MissingValue(argument);
                    draft.RecordingDirectory = Path.GetFullPath(rawRecord);
                    break;

                case "--plugin-dir":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawPluginDir)) return ArgumentCursor.MissingValue(argument);
                    if (!Directory.Exists(rawPluginDir)) return ArgumentCursor.Fail($"plugin directory '{rawPluginDir}' does not exist.");
                    draft.PluginDirectory = Path.GetFullPath(rawPluginDir);
                    break;

                // Not required to exist: a host pointed at a store before anything is installed
                // must report it empty rather than refuse to start.
                case "--extension-dir":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawExtensionDir)) return ArgumentCursor.MissingValue(argument);
                    draft.ExtensionDirectory = Path.GetFullPath(rawExtensionDir);
                    break;

                // Not validated here: the value may be a URL, and a local path is checked by the
                // fetch itself so an index deleted between start-up and the fetch still reports
                // unreachable rather than being pre-cleared as fine.
                case "--extensions" or "-x":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawCatalogue)) return ArgumentCursor.MissingValue(argument);
                    draft.ExtensionCatalogue = rawCatalogue;
                    break;

                // Not validated beyond its shape: reaching Slack at start-up to prove a webhook
                // works would post a message nobody asked for.
                case "--slack-webhook":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawSlack)) return ArgumentCursor.MissingValue(argument);
                    if (!Uri.TryCreate(rawSlack, UriKind.Absolute, out _)) return ArgumentCursor.Fail($"'{rawSlack}' is not an absolute webhook URL.");
                    draft.SlackWebhook = rawSlack;
                    break;

                case "--mqtt":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawBroker)) return ArgumentCursor.MissingValue(argument);
                    if (!ArgumentCursor.TryHostAndPort(rawBroker, HostOptions.DefaultMqttPort, out draft.MqttBrokerHost!, out draft.MqttBrokerPort))
                    {
                        return ArgumentCursor.Fail($"'{rawBroker}' is not a broker address; expected host or host:port.");
                    }
                    break;

                case "--mqtt-topic":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawTopic)) return ArgumentCursor.MissingValue(argument);
                    if (rawTopic.Contains('+') || rawTopic.Contains('#')) return ArgumentCursor.Fail("a topic prefix may not contain the MQTT wildcards '+' or '#'.");
                    draft.MqttTopicPrefix = rawTopic;
                    break;

                case "--check-updates":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawRepo)) return ArgumentCursor.MissingValue(argument);
                    draft.UpdateRepository = rawRepo;
                    break;

                case "--sse":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawSse)) return ArgumentCursor.MissingValue(argument);
                    if (!Uri.TryCreate(rawSse, UriKind.Absolute, out Uri? sseUri)
                        || (sseUri.Scheme != Uri.UriSchemeHttp && sseUri.Scheme != Uri.UriSchemeHttps))
                    {
                        return ArgumentCursor.Fail($"'{rawSse}' is not an absolute http(s) URL.");
                    }
                    draft.SseEndpoint = rawSse;
                    break;

                // Checked here because a map that cannot be read must stop the start rather than
                // producing a host that connects to the feed and silently charts nothing.
                case "--stream-map":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawMap)) return ArgumentCursor.MissingValue(argument);
                    if (!File.Exists(rawMap)) return ArgumentCursor.Fail($"channel map '{rawMap}' does not exist.");
                    draft.ChannelMapPath = Path.GetFullPath(rawMap);
                    break;

                // Parsed here rather than accepted verbatim, so a malformed expression stops the
                // start. A host that accepted it would serve that channel as permanently
                // unavailable, which is indistinguishable from a sensor that has gone quiet.
                case "--computed":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawComputed)) return ArgumentCursor.MissingValue(argument);
                    try
                    {
                        TelemetryDashboard.Core.Analytics.ComputedChannel.Parse(rawComputed);
                    }
                    catch (FormatException ex)
                    {
                        return ArgumentCursor.Fail($"--computed {rawComputed}: {ex.Message}");
                    }
                    draft.Computed.Add(rawComputed);
                    break;

                // Parsed at the command line for the same reason as --computed, and more urgently:
                // a limit that does not parse is a limit that never fires, and a silent alarm has
                // no other symptom.
                case "--limit":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawLimit)) return ArgumentCursor.MissingValue(argument);
                    try
                    {
                        TelemetryDashboard.Core.Analytics.ChannelLimit.Parse(rawLimit);
                    }
                    catch (FormatException ex)
                    {
                        return ArgumentCursor.Fail($"--limit {rawLimit}: {ex.Message}");
                    }
                    draft.Limits.Add(rawLimit);
                    break;

                // A limit that also acts. Kept apart from --limit because the two are different
                // authorisations, and this one writes to the machine.
                case "--emergency-limit":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawTrip)) return ArgumentCursor.MissingValue(argument);
                    try
                    {
                        TelemetryDashboard.Core.Analytics.ChannelLimit.Parse(rawTrip);
                    }
                    catch (FormatException ex)
                    {
                        return ArgumentCursor.Fail($"--emergency-limit {rawTrip}: {ex.Message}");
                    }
                    draft.EmergencyLimits.Add(rawTrip);
                    draft.Limits.Add(rawTrip);
                    break;

                // A reference signal, refused here if it cannot be one. A declaration that does not
                // parse, or a rate above what the simulator samples at, produces a spectrum peak
                // that is real, wrong and has no symptom -- and this feature exists precisely to be
                // the thing other measurements are checked against.
                case "--signal":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawSignal)) return ArgumentCursor.MissingValue(argument);
                    try
                    {
                        TelemetryDashboard.Core.Analytics.InjectedSignal.Parse(rawSignal);
                    }
                    catch (FormatException ex)
                    {
                        return ArgumentCursor.Fail($"--signal {rawSignal}: {ex.Message}");
                    }
                    draft.Signals.Add(rawSignal);
                    break;

                case "--archive":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawArchive)) return ArgumentCursor.MissingValue(argument);
                    draft.ArchivePath = Path.GetFullPath(rawArchive);
                    break;

                case "--expect":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawExpect)) return ArgumentCursor.MissingValue(argument);
                    if (NodeIdList.Parse(rawExpect) is not { Count: > 0 } expected)
                    {
                        return ArgumentCursor.Fail($"--expect needs at least one node id, not '{rawExpect}'.");
                    }
                    draft.ExpectedNodes = expected;
                    break;

                case "--retire":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawRetire)) return ArgumentCursor.MissingValue(argument);
                    if (NodeIdList.Parse(rawRetire) is not { Count: > 0 } retired)
                    {
                        return ArgumentCursor.Fail($"--retire needs at least one node id, not '{rawRetire}'.");
                    }
                    draft.RetiredNodes = retired;
                    break;

                case "--credential":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawCredential)) return ArgumentCursor.MissingValue(argument);
                    if (!File.Exists(rawCredential))
                    {
                        // Refused at parse time, like --rules. An operator who mistyped the path
                        // must not get a console that serves openly because its lock was missing.
                        return ArgumentCursor.Fail(
                            $"credential file '{rawCredential}' does not exist. Serving without the "
                            + "credential that was asked for would be the opposite of what was asked.");
                    }
                    if (Core.Security.CredentialFile.Load(rawCredential) is null)
                    {
                        // Load answers null for an unreadable or malformed file, which is the right
                        // answer for the screen lock -- it lets an operator enroll a new password
                        // instead of being locked out. Here the same answer would mean serving with
                        // no credential at all, so it is refused rather than defaulted.
                        return ArgumentCursor.Fail(
                            $"credential file '{rawCredential}' could not be read as one. It holds a "
                            + "salted PBKDF2 derivation written by the screen-lock enrollment.");
                    }
                    draft.CredentialPath = Path.GetFullPath(rawCredential);
                    break;

                case "--backfill":
                    draft.Backfill = true;
                    break;
                case "--listen":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawListen)) return ArgumentCursor.MissingValue(argument);
                    switch (rawListen)
                    {
                        case "loopback": draft.ListenOnAllInterfaces = false; break;
                        case "network": draft.ListenOnAllInterfaces = true; break;
                        default:
                            // Named values rather than a bare --listen-network switch, so the
                            // default has a name a script can write down. An operator pinning
                            // 'loopback' explicitly should not have to express it as the absence
                            // of a flag, which is indistinguishable from having forgotten it.
                            return ArgumentCursor.Fail(
                                $"--listen takes 'loopback' or 'network', not '{rawListen}'.");
                    }
                    break;

                case "--rules":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawRules)) return ArgumentCursor.MissingValue(argument);
                    if (!File.Exists(rawRules)) return ArgumentCursor.Fail($"rule file '{rawRules}' does not exist.");
                    draft.RulesPath = Path.GetFullPath(rawRules);
                    break;

                case "--coverage-state":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawCoverage)) return ArgumentCursor.MissingValue(argument);
                    draft.CoverageStatePath = Path.GetFullPath(rawCoverage);
                    break;

                case "--replay":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawReplay)) return ArgumentCursor.MissingValue(argument);
                    if (!File.Exists(rawReplay)) return ArgumentCursor.Fail($"recording '{rawReplay}' does not exist.");
                    draft.ReplayPath = Path.GetFullPath(rawReplay);
                    break;

                case "--replay-speed":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawSpeed)) return ArgumentCursor.MissingValue(argument);
                    if (!double.TryParse(rawSpeed, NumberStyles.Float, CultureInfo.InvariantCulture, out double speed)
                        || !double.IsFinite(speed) || speed <= 0)
                    {
                        return ArgumentCursor.Fail($"'{rawSpeed}' is not a positive replay speed.");
                    }
                    draft.ReplaySpeed = speed;
                    break;

                case "--emergency-stop":
                    draft.EmergencyStop = true;
                    break;

                case "--emergency-sigma":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawSigma)) return ArgumentCursor.MissingValue(argument);
                    if (!double.TryParse(rawSigma, NumberStyles.Float, CultureInfo.InvariantCulture, out double sigma)
                        || !double.IsFinite(sigma) || sigma <= 0)
                    {
                        return ArgumentCursor.Fail($"'{rawSigma}' is not a positive sigma threshold.");
                    }
                    draft.EmergencySigma = sigma;
                    break;

                case "--emergency-command":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawCommand)) return ArgumentCursor.MissingValue(argument);
                    if (string.IsNullOrWhiteSpace(rawCommand))
                    {
                        return ArgumentCursor.Fail("--emergency-command needs a command to transmit.");
                    }
                    draft.EmergencyCommand = rawCommand;
                    break;

                case "--emergency-cooldown":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawCooldown)) return ArgumentCursor.MissingValue(argument);
                    if (!double.TryParse(rawCooldown, NumberStyles.Float, CultureInfo.InvariantCulture, out double cooldown)
                        || !double.IsFinite(cooldown) || cooldown < 0)
                    {
                        return ArgumentCursor.Fail($"'{rawCooldown}' is not a cooldown in seconds.");
                    }
                    draft.EmergencyCooldownSec = cooldown;
                    break;

                case "--export-dashboard":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawExport)) return ArgumentCursor.MissingValue(argument);
                    // The directory has to exist; creating one the operator did not ask for is how
                    // a mistyped path silently becomes a new folder nobody looks in again.
                    string exportFull = Path.GetFullPath(rawExport);
                    string? exportDir = Path.GetDirectoryName(exportFull);
                    if (!string.IsNullOrEmpty(exportDir) && !Directory.Exists(exportDir))
                    {
                        return ArgumentCursor.Fail($"directory '{exportDir}' does not exist.");
                    }
                    draft.DashboardExportPath = exportFull;
                    break;

                case "--profile":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawProfile)) return ArgumentCursor.MissingValue(argument);
                    draft.ProfileId = rawProfile;
                    break;

                case "--poll":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawPoll)) return ArgumentCursor.MissingValue(argument);
                    if (!Uri.TryCreate(rawPoll, UriKind.Absolute, out Uri? pollUri)
                        || (pollUri.Scheme != Uri.UriSchemeHttp && pollUri.Scheme != Uri.UriSchemeHttps))
                    {
                        return ArgumentCursor.Fail($"'{rawPoll}' is not an absolute http(s) URL.");
                    }
                    draft.PollEndpoint = rawPoll;
                    break;

                case "--poll-interval":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? rawInterval)) return ArgumentCursor.MissingValue(argument);
                    if (!double.TryParse(rawInterval, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double seconds)
                        || TimeSpan.FromSeconds(seconds) < PollingTelemetrySource.MinimumInterval)
                    {
                        return ArgumentCursor.Fail(
                            $"'{rawInterval}' is not a poll interval of at least "
                            + $"{PollingTelemetrySource.MinimumInterval.TotalMilliseconds:N0} ms. Public feeds are shared.");
                    }
                    draft.PollInterval = TimeSpan.FromSeconds(seconds);
                    break;

                default:
                    return ArgumentCursor.Fail($"unknown argument '{argument}'.");
            }
        }

        // Both sources at once would mean broadcasting synthetic and measured frames on one
        // channel, with nothing downstream able to separate them again.
        // A profile named alongside a real device used to be refused, and the reason given was that
        // it would do nothing: the feature setup consulted a profile only for generated sources, so
        // the flag was accepted and ignored. That premise no longer holds. A profile also declares
        // the rig's safe bands, its derived channels and where its nodes sit, and all three apply
        // to hardware -- an operator reading an MCU had to restate every band on the command line
        // that the profile they already have states once.
        //
        // What a profile still does not decide for a real device is what it sends. That is the
        // rule file's job, and the two are separate on purpose: the profile describes the rig, the
        // rules describe this firmware's spelling of it.
        bool generates = draft.Simulate
            || string.Equals(draft.SerialPort, "loopback", StringComparison.OrdinalIgnoreCase);

        // Refused rather than ignored, and refused rather than allowed to pick a port itself. This
        // is the only flag that transmits to hardware, so the port it writes to has to be one the
        // operator named -- and the controller behind it defaults to a rule aimed at COM3, which
        // would otherwise be where an unqualified --emergency-stop ended up pointing.
        if (draft.Signals.Count > 0 && !generates)
        {
            return ArgumentCursor.Fail(
                "--signal needs a generated source (--simulate or --serial loopback). On a real rig "
                + "the channel reads what the converter is doing, and this host does not decide it.");
        }

        // Checked here rather than beside the flag because argument order must not decide it:
        // '--listen network --credential f' and '--credential f --listen network' are the same
        // request, and an inline check would accept one and refuse the other.
        // Checked here rather than beside the flag because argument order must not decide it:
        // '--listen network --credential f' and '--credential f --listen network' are the same
        // request, and an inline check would accept one and refuse the other.
        if (draft.ListenOnAllInterfaces && draft.CredentialPath is null)
        {
            return ArgumentCursor.Fail(
                "--listen network needs --credential. This console streams live telemetry, replays "
                + "recorded incidents and accepts commands over its WebSocket; on a shared segment "
                + "an open listener publishes all of it to whoever is on that segment. Enrol one "
                + "with: telemetry-host credential --out console.cred");
        }

        // Opt-in, like --retain and --archive, because it makes network requests the operator did
        // not otherwise ask for -- to a peer, on a link that has just proved itself unreliable.
        if (draft.Backfill && draft.SseEndpoint is null)
        {
            return ArgumentCursor.Fail(
                "--backfill needs --sse: it asks the peer this host reads from for the interval a dropped "
                + "link cost, and a host with no upstream has no peer to ask.");
        }

        if (draft.EmergencyLimits.Count > 0 && !draft.EmergencyStop)
        {
            return ArgumentCursor.Fail(
                "--emergency-limit needs --emergency-stop. A limit that says it will act on the "
                + "machine, on a host that cannot, is worse than no limit: it reads as protection.");
        }

        if (draft.EmergencyStop && draft.SerialPort is null)
        {
            return ArgumentCursor.Fail(
                "--emergency-stop needs --serial: the interlock transmits to the port you opened, "
                + "and this host will not choose a port to write to on its own.");
        }

        if (!draft.EmergencyStop
            && (draft.EmergencySigma != HostOptions.DefaultEmergencySigma
                || draft.EmergencyCommand != HostOptions.DefaultEmergencyCommand
                || draft.EmergencyCooldownSec != HostOptions.DefaultEmergencyCooldownSec))
        {
            return ArgumentCursor.Fail(
                "--emergency-sigma, --emergency-command and --emergency-cooldown only apply with "
                + "--emergency-stop; tuning an interlock that is switched off does nothing.");
        }

        // One host reads one source. A replay mixed with a live feed would interleave last week's
        // frames with this minute's on one timeline, and nothing downstream could separate them.
        if (draft.ReplayPath is not null
            && (draft.SerialPort is not null || draft.Simulate
                || draft.SseEndpoint is not null || draft.PollEndpoint is not null))
        {
            return ArgumentCursor.Fail("--replay cannot be combined with another source: one host reads one source.");
        }

        if (draft.ReplayPath is null && draft.ReplaySpeed != HostOptions.DefaultReplaySpeed)
        {
            return ArgumentCursor.Fail("--replay-speed only applies with --replay.");
        }

        if (draft.PollEndpoint is not null && (draft.SseEndpoint is not null || draft.SerialPort is not null || draft.Simulate))
        {
            return ArgumentCursor.Fail("--poll cannot be combined with another source: one host reads one source.");
        }

        if (draft.SseEndpoint is not null && (draft.SerialPort is not null || draft.Simulate))
        {
            return ArgumentCursor.Fail("--sse cannot be combined with --serial or --simulate: one host reads one source.");
        }

        if (draft.SerialPort is not null && draft.Simulate)
        {
            return ArgumentCursor.Fail("--serial and --simulate are mutually exclusive: pick measured data or synthetic data.");
        }

        return draft.Build();
    }
}
