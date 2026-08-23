using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Serial;
using TelemetryDashboard.Core.Events;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// Measured telemetry, read from a serial port through <see cref="MultiPortSerialManager"/>.
/// </summary>
/// <remarks>
/// One port per host process, deliberately: the manager handles many, but a headless instance
/// configured by flags has no way to express per-port routing, and a second port silently sharing
/// one instance's channel names would blur two devices together. Run a second host instead.
/// </remarks>
public sealed class SerialTelemetrySource : ITelemetrySource
{
    private readonly MultiPortSerialManager _manager = new();
    private readonly string _portName;
    private readonly int _baudRate;

    private AutoReconnectEngine? _reconnect;

    /// <summary>Opens nothing yet; call <see cref="OpenAsync"/>.</summary>
    /// <param name="autoReconnect">
    /// Whether to re-open the port after it dies. On by default: an unattended plant host that
    /// stops at the first cable glitch and waits for a human has failed at the one job it was left
    /// alone to do. Reconnecting also sends this repository's <c>$CMD,REQ_RESYNC</c> line so the
    /// device can backfill the gap — the same protocol assumption the default routing rules already
    /// make. Pass false for a device that must never receive unsolicited bytes.
    /// </param>
    public SerialTelemetrySource(string portName, int baudRate, bool autoReconnect = true)
    {
        _portName = portName;
        _baudRate = baudRate;
        AutoReconnect = autoReconnect;
    }

    /// <summary>Whether the port is watched and re-opened after a failure.</summary>
    public bool AutoReconnect { get; }

    /// <summary>Times the port has died since it was first opened.</summary>
    public int FaultCount { get; private set; }

    /// <summary>Times the port has come back after a failure.</summary>
    public int RecoveryCount { get; private set; }

    /// <inheritdoc />
    public string Origin => "REAL_HARDWARE";

    /// <inheritdoc />
    public bool IsSimulated => false;

    /// <inheritdoc />
    public string Description => $"{_portName} @ {_baudRate} baud";

    /// <summary>Whether the port is open.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The manager holding this source's port.</summary>
    /// <remarks>
    /// Exposed for the plugin host, which must hand plugins the manager whose port is open rather
    /// than a new one: <see cref="ISerialManager.ActivePorts"/> on a fresh instance is empty, so a
    /// plugin would conclude no device is attached while frames are arriving beside it. Ownership
    /// stays here — <see cref="DisposeAsync"/> is the only thing that disposes it.
    /// </remarks>
    public ISerialManager SerialManager => _manager;

    /// <summary>Opens the port. Returns false when the device is absent or already claimed.</summary>
    public async Task<bool> OpenAsync(CancellationToken cancellationToken)
    {
        IsOpen = await _manager.ConnectPortAsync(_portName, _baudRate, cancellationToken).ConfigureAwait(false);
        if (!IsOpen || !AutoReconnect) return IsOpen;

        _manager.PortFaulted += OnPortFaulted;
        _manager.PortRecovered += OnPortRecovered;

        // Polling, because a headless Windows process has no message pump to deliver
        // WM_DEVICECHANGE and would otherwise never learn the device came back.
        if (!_manager.HotPlugDetectionActive) _manager.EnablePortPolling();

        _reconnect = new AutoReconnectEngine(_manager, ReconnectInterval);
        _reconnect.RegisterTargetPort(_portName, _baudRate);
        _reconnect.Start();

        return true;
    }

    /// <summary>How often a dead port is retried.</summary>
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(2);

    private void OnPortFaulted(object? sender, SerialPortFaultEventArgs e)
    {
        FaultCount++;
        IsOpen = false;
        Console.Error.WriteLine(
            $"[serial] {e.Describe()}. Retrying every {ReconnectInterval.TotalSeconds:0.#}s. "
            + "No frames will arrive until it comes back.");
    }

    private void OnPortRecovered(object? sender, string portName)
    {
        RecoveryCount++;
        IsOpen = true;
        Console.Error.WriteLine($"[serial] {portName} reconnected after {FaultCount} failure(s); resync requested.");
    }

    /// <inheritdoc />
    public IAsyncEnumerable<RawPacket> ReadAsync(CancellationToken cancellationToken) =>
        _manager.PacketReader.ReadAllAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        IsOpen = false;

        // Before the manager: the engine reconnects any registered port it finds down, so tearing
        // the ports down first would race it into re-opening what we are trying to close.
        if (_reconnect is not null)
        {
            await _reconnect.StopMonitoringAsync().ConfigureAwait(false);
            await _reconnect.DisposeAsync().ConfigureAwait(false);
        }

        _manager.PortFaulted -= OnPortFaulted;
        _manager.PortRecovered -= OnPortRecovered;

        await _manager.DisconnectAllAsync().ConfigureAwait(false);
        await _manager.DisposeAsync().ConfigureAwait(false);
    }
}
