using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Host.Ingest;
using TelemetryDashboard.Infrastructure.WebServer;

using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Host.Outbound;

/// <summary>
/// Starts whichever outbound relays the operator configured, and nothing else.
/// </summary>
/// <remarks>
/// Every relay here is opt-in by an explicit flag. The host will not post to a workspace or a
/// broker because a default pointed at one — sending messages on an operator's behalf that they
/// never asked for is worse than sending none.
///
/// It also refuses to imply a capability it does not have: a relay configured while no source is
/// attached says so at start-up instead of sitting silently and letting the operator believe
/// alerts are armed.
/// </remarks>
public sealed class OutboundRelays : IAsyncDisposable
{
    private readonly List<string> _banner = new();
    private SlackAlertRelay? _slack;
    private MqttTelemetryRelay? _mqtt;
    private EmergencyInterlockRelay? _emergency;

    private OutboundRelays() { }

    /// <summary>Lines describing what is armed, for the start-up banner.</summary>
    public IReadOnlyList<string> BannerLines => _banner;

    /// <summary>Whether anything at all is relaying.</summary>
    public bool IsActive => _slack is not null || _mqtt is not null || _emergency is not null;

    /// <summary>Builds and subscribes the configured relays.</summary>
    public static async Task<OutboundRelays> StartAsync(
        HostOptions options, TelemetryIngestPump? pump, ISerialManager? serialManager = null)
    {
        var relays = new OutboundRelays();

        if (options.SlackWebhook is not null)
        {
            relays._slack = new SlackAlertRelay(new SlackClient(), options.SlackWebhook);
            relays._banner.Add($"  alerts        Slack, one message per channel per {SlackAlertRelay.DefaultCooldown.TotalMinutes:0} min");
        }

        if (options.MqttBrokerHost is not null)
        {
            var relay = new MqttTelemetryRelay(new MqttPublisher(), options.MqttTopicPrefix);
            bool connected = await relay.ConnectAsync(options.MqttBrokerHost, options.MqttBrokerPort).ConfigureAwait(false);

            relays._mqtt = relay;
            relays._banner.Add(connected
                ? $"  mqtt          {options.MqttBrokerHost}:{options.MqttBrokerPort} -> {options.MqttTopicPrefix}/<node>/<variable>"
                : $"  mqtt          UNREACHABLE -- {options.MqttBrokerHost}:{options.MqttBrokerPort}");

            if (!connected)
            {
                relays._banner.Add("                Samples will be queued and dropped when the queue fills; the");
                relays._banner.Add("                count of what was lost is reported at shutdown.");
            }
        }

        relays._emergency = EmergencyInterlockRelay.Start(options, serialManager);
        if (relays._emergency is not null)
        {
            relays._banner.Add(
                $"  emergency     ARMED -- transmits '{options.EmergencyCommand.Trim()}' "
                + $"to {options.SerialPort}, at most once per channel every "
                + $"{options.EmergencyCooldownSec:0.#}s");
            relays._banner.Add(
                $"                on  above {options.EmergencySigma:0.#} sigma");

            // Named rather than summarised. An armed interlock that does not say what trips it
            // leaves an operator reading a sigma threshold and assuming that is all of it, and the
            // limits are the half that fires on a steady fault the sigma cannot see.
            foreach (string trip in options.EmergencyLimits)
            {
                relays._banner.Add($"                and outside {trip}");
            }

            if (options.EmergencyLimits.Count == 0)
            {
                relays._banner.Add(
                    "                NOTE: sigma only. A channel sitting steadily outside a safe "
                    + "band is not unusual to a rolling detector and will not trip this. "
                    + "--emergency-limit adds a band that will.");
            }

            relays._banner.Add(
                string.Equals(options.SerialPort, "loopback", StringComparison.OrdinalIgnoreCase)
                    ? "                Writes go to an in-memory port, not to hardware."
                    : "                This host writes to your hardware.");
        }

        relays.Subscribe(pump);
        return relays;
    }

    private void Subscribe(TelemetryIngestPump? pump)
    {
        if (!IsActive) return;

        if (pump is null)
        {
            _banner.Add("                No source is attached, so nothing will be relayed. Add");
            _banner.Add("                --serial or --simulate for these to carry anything.");
            return;
        }

        if (_slack is not null) pump.SampleScored += _slack.OnSampleScored;
        if (_mqtt is not null) pump.SampleScored += _mqtt.OnSampleScored;
        if (_emergency is not null) pump.SampleScored += _emergency.OnSampleScored;
    }

    /// <summary>What each relay actually delivered, for the shutdown report.</summary>
    public IReadOnlyList<string> Summary()
    {
        var lines = new List<string>();
        if (_slack?.Summary() is { } slack) lines.Add("           " + slack);
        if (_mqtt?.Summary() is { } mqtt) lines.Add("           " + mqtt);
        if (_emergency?.Summary() is { } emergency) lines.Add("           " + emergency);
        return lines;
    }

    public async ValueTask DisposeAsync()
    {
        if (_slack is not null) await _slack.DisposeAsync().ConfigureAwait(false);
        if (_mqtt is not null) await _mqtt.DisposeAsync().ConfigureAwait(false);
        if (_emergency is not null) await _emergency.DisposeAsync().ConfigureAwait(false);
    }
}
