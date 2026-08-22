using TelemetryDashboard.Host.Outbound;
using TelemetryDashboard.Host.Startup;

using TelemetryDashboard.Host.Ingest;

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
          -s, --serial loopback
                                An in-memory port with nothing behind it. Frames are generated
                                from the profile and sent through the port's own buffer, and
                                anything the host writes back is announced as
                                "[loopback] LOOPBACK <= <command>". This exists so the emergency
                                interlock -- refused without --serial, and the only feature that
                                acts on the machine -- can be exercised on a workstation with no
                                MCU attached. It proves the host wrote the command to the port it
                                was told to, and nothing about drivers, cables or devices.
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
              --archive <file>  Keep a durable SQLite archive of every ingested sample, queryable
                                afterwards at /api/history by node, channel and time window. A CSV
                                recording is a transcript; this is a store you can ask questions of,
                                and it is the only thing here that outlives the process.
              --computed <decl> Serve a channel computed from others, written as
                                "id[unit] = expression". Repeatable. Every input is aligned to one
                                instant before the expression runs, and no value is published
                                unless all of them answer that instant -- a power multiplied from a
                                voltage now and a current from 300 ms ago was never drawn. Example:
                                --computed "psfb.efficiency[%] = 100 * psfb.output_voltage *
                                psfb.output_current / (dab.bus_voltage * dab.input_current)"
                                Served at /api/computed, and always marked derived.
              --emergency-limit <decl>
                                A limit that also trips the interlock, not merely raises an
                                alarm. Needs --emergency-stop. Kept separate from --limit because
                                acting on the machine is a different authorisation from telling
                                somebody to look, and because a converter shut down for a
                                two-sample overshoot is a converter whose interlock gets disabled
                                by the end of the week. Unlike the sigma threshold, this fires
                                during warm-up: a reading outside a hard limit is outside it
                                before any baseline exists.
                                A generated run also serves /api/control, which is the one place
                                this product is not read-only: GET lists the channels and
                                scenarios that may be moved, POST moves one. It exists so an
                                engineer can prove the alarm fires and the interlock trips without
                                over-volting real hardware. A POST must carry a Content-Length,
                                even a zero one -- Windows' HTTP stack answers 411 before this
                                host sees the request. Browsers do this; curl needs -d "".
                                  curl -X POST -d "" ".../api/control?cmd=setpoint&channel=dab.bus_voltage&value=440"
                                A host reading a real device offers none of it: acting on that
                                machine is the emergency interlock's job, armed separately.
              --signal <decl>   Drive a channel with a known waveform instead of the simulator's
                                drift, written as "channel=shape@frequencyHz:amplitude" -- e.g.
                                "dab.bus_voltage=sine@2:20". Repeatable, and only with a generated
                                source. This exists so the analysis can be checked rather than
                                trusted: ask for 2 Hz and /api/spectrum should report 2 Hz. It did,
                                to 0.55 of its own bin width -- and the first run found the
                                generator, not the analyser, to be the thing that was wrong.
                                Shapes: sine, square, triangle, sawtooth, noise. A rate above the
                                simulator's Nyquist limit is refused, but only the fundamental is
                                checked: every shape but sine has harmonics, and those fold too.
              --limit <decl>    Enforce an engineering limit, written as
                                "channel[unit] in lo..hi", or a channel followed by >, >=, <
                                or <= and a number. Repeatable. This is the alarm a rolling
                                detector cannot raise: a z-score asks how unusual a reading is
                                against the channel's own recent history, so a bus that settles
                                above its ceiling becomes normal to it within a minute. A limit
                                asks whether the reading is safe, which does not move. The unit
                                is checked against what the channel reports and the rule is
                                disarmed if they disagree, because a limit that can never fire
                                looks exactly like a healthy machine. State at /api/limits.
                                Pair it with --replay to make an old CSV recording queryable:
                                a transcript cannot be asked about one channel last Tuesday, and
                                replaying it into an archive turns it into a store that can be.
                                Measured at 990 rows in, 990 samples queryable out.
              --replay <file>   Play a recorded CSV back through the live pipeline instead of
                                reading a source. Routing, analytics, the console, the spectrum
                                and the DVR all behave as they do live; every frame says REPLAY,
                                so a recording is never mistaken for a current reading. The
                                recorded score is dropped and recomputed, because a stored verdict
                                disagrees with the detector after any change to it.
              --replay-speed <x>
                                How fast to play back. Default {HostOptions.DefaultReplaySpeed}, meaning real time. Gaps
                                longer than {ReplayTelemetrySource.MaximumGapSec:0.#}s are compressed and counted.
              --profile <id>    Which monitoring profile to use. Applies to --simulate and to
                                --export-dashboard. An unknown id is refused and the available
                                ids are listed, rather than quietly falling back.
              --export-dashboard <file>
                                Write a standalone HTML console for the active profile: one card
                                and one trend per declared channel, in that channel's own unit and
                                range. Open it while this host runs; it connects back over the
                                WebSocket. The directory must already exist.
              --emergency-stop  Transmit a command back to the device when a channel is judged
                                past --emergency-sigma. OFF by default and refused without
                                --serial: this is the only flag that makes the host act on your
                                hardware, and it writes only to the port you opened.
              --emergency-sigma <n>
                                Sigma at which the interlock fires. Default {HostOptions.DefaultEmergencySigma}.
              --emergency-command <text>
                                What to transmit. Default '{HostOptions.DefaultEmergencyCommand}'.
              --emergency-cooldown <sec>
                                Seconds before the same channel may fire again. Default
                                {HostOptions.DefaultEmergencyCooldownSec}. Triggers held back are counted and reported.
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
