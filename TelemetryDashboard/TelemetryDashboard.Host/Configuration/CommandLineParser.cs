using System;
using System.Collections.Generic;
using System.IO;

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

                default:
                    return ArgumentCursor.Fail($"unknown argument '{argument}'.");
            }
        }

        // Both sources at once would mean broadcasting synthetic and measured frames on one
        // channel, with nothing downstream able to separate them again.
        if (draft.SerialPort is not null && draft.Simulate)
        {
            return ArgumentCursor.Fail("--serial and --simulate are mutually exclusive: pick measured data or synthetic data.");
        }

        return draft.Build();
    }
}
