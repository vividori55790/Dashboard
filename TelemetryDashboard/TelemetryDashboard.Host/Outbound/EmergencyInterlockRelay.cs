using System;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Host.Ingest;

namespace TelemetryDashboard.Host.Outbound;

/// <summary>
/// Transmits a command back to the device when a channel is judged far enough out of range.
/// </summary>
/// <remarks>
/// Feature 12 in this project's inventory, marked Built since M3. <see cref="EmergencyMcuController"/>
/// existed, was tested, and was constructed by nothing — so the one feature that acts on the
/// machine rather than watching it could not be reached from any running program.
/// <para>
/// It stays off unless <c>--emergency-stop</c> is given, and that flag is refused without
/// <c>--serial</c>. Both refusals are deliberate. The controller ships a default rule that
/// auto-executes against a port literally named <c>COM3</c>, so a host that armed itself would be
/// writing to whatever happened to be on that port on that machine. The interlock here writes only
/// to the port the operator opened, and only with the command they supplied.
/// </para>
/// <para>
/// The decision is made on the ingest thread; the write is not. The controller is given a dispatch
/// callback that hands the command to a bounded queue, the same shape the Slack and MQTT relays
/// use, so a serial port that has stopped accepting writes cannot stall ingest. The console, the
/// recording and every other channel's scoring are still worth having while one port is wedged.
/// </para>
/// </remarks>
public sealed class EmergencyInterlockRelay : IAsyncDisposable
{
    /// <summary>Commands held while the port is busy. Small on purpose.</summary>
    /// <remarks>
    /// A deep queue would let a burst schedule dozens of identical safe-mode commands to arrive
    /// long after the condition that caused them. The cooldown already collapses a storm into one
    /// dispatch; this bounds what a wedged port can accumulate behind it, and what it could not
    /// hold is counted rather than dropped in silence.
    /// </remarks>
    private const int QueueCapacity = 16;

    private readonly EmergencyMcuController _controller;
    private readonly OutboundQueue<string> _queue;
    private readonly string _port;
    private long _fired;

    private EmergencyInterlockRelay(EmergencyMcuController controller, OutboundQueue<string> queue, string port)
    {
        _controller = controller;
        _queue = queue;
        _port = port;
    }

    /// <summary>Triggers the controller dispatched.</summary>
    public long Fired => Interlocked.Read(ref _fired);

    /// <summary>Triggers held back because one had just been sent for the same channel.</summary>
    public long SuppressedByCooldown => _controller.SuppressedByCooldown;

    /// <summary>Commands sent, failed and dropped, for the shutdown report.</summary>
    public OutboundTally Tally => _queue.Tally;

    /// <summary>Arms the interlock, or returns null when it was not asked for.</summary>
    public static EmergencyInterlockRelay? Start(HostOptions options, ISerialManager? serialManager)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.EmergencyStop || options.SerialPort is null || serialManager is null) return null;

        string port = options.SerialPort;

        var queue = new OutboundQueue<string>(
            "emergency",
            QueueCapacity,
            (command, token) => serialManager.WriteLineAsync(port, command, token));

        // The controller decides and keeps the history; the queue does the writing. Handing it the
        // serial manager instead would put a port write on the ingest thread.
        var controller = new EmergencyMcuController(dispatchCallback: (_, command) =>
        {
            queue.Offer(command);
            return Task.CompletedTask;
        });

        // The constructor's own rule is discarded rather than added to. It targets COM3 and
        // auto-executes, and a rule list where one entry is the operator's and another is a
        // leftover default is a list nobody can reason about.
        controller.ClearRules();
        controller.RegisterRule(new EmergencyRule
        {
            ChannelName = "*",
            ZScoreThreshold = options.EmergencySigma,
            CommandTxPayload = options.EmergencyCommand,
            AutoExecute = true,
            CooldownSec = options.EmergencyCooldownSec,
            TargetPort = port
        });

        return new EmergencyInterlockRelay(controller, queue, port);
    }

    /// <summary>
    /// Decides on one scored sample. Never throws, and does not wait on the port.
    /// </summary>
    /// <remarks>
    /// A sample with no verdict is ignored outright. During warm-up the engine has no baseline, so
    /// its z-score is not a small number — it is no number at all, and treating it as one would let
    /// the first readings of a run trip an interlock on a machine nobody has measured yet.
    /// </remarks>
    public void OnSampleScored(object? sender, ScoredSample sample)
    {
        if (sample.ZScore is not double z) return;

        Task<bool> decision = _controller.EvaluateAndDispatchAsync(_port, sample.Channel, z, sample.Value);

        // The dispatch callback only enqueues, so this completes inline in the ordinary case and
        // no continuation is scheduled at all.
        if (decision.IsCompletedSuccessfully)
        {
            if (decision.Result) Interlocked.Increment(ref _fired);
            return;
        }

        _ = ObserveAsync(decision, sample, z);
    }

    /// <summary>Observes a decision that did not complete inline, so a fault cannot be lost.</summary>
    private async Task ObserveAsync(Task<bool> decision, ScoredSample sample, double z)
    {
        try
        {
            if (await decision.ConfigureAwait(false)) Interlocked.Increment(ref _fired);
        }
        catch (Exception ex)
        {
            // An interlock that failed to act is the single most important thing this host can
            // say, so it is said immediately rather than saved for a shutdown summary.
            Console.Error.WriteLine(
                $"telemetry-host: EMERGENCY INTERLOCK FAILED for {sample.Channel} (z={z:F2}) "
                + $"on {_port}: {ex.Message}");
        }
    }

    /// <summary>One line for the shutdown report, or null when the interlock never fired.</summary>
    public string? Summary()
    {
        if (Fired == 0 && SuppressedByCooldown == 0) return null;

        string transport = _queue.Summary() is { } q ? $", {q}" : string.Empty;
        return $"emergency: {Fired} dispatch(es) to {_port}, "
            + $"{SuppressedByCooldown} suppressed by cooldown{transport}";
    }

    public async ValueTask DisposeAsync() => await _queue.DisposeAsync().ConfigureAwait(false);
}
