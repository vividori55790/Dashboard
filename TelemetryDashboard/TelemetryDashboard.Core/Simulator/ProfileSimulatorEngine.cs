using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Parsers;

namespace TelemetryDashboard.Core.Simulator;

/// <summary>
/// Generates synthetic frames for whatever system a <see cref="MonitoringProfile"/> describes.
/// </summary>
/// <remarks>
/// The engine it replaces was one customer's rig written into code: two nodes on COM3 and COM4,
/// four channels called TEMP, VIB, RPM and VOLT, and waveforms tuned to their voltages. Selecting a
/// different profile changed the sliders and the labels while the data underneath stayed theirs, so
/// an operator watching a kiln was reading a battery converter with new captions on it.
///
/// What this produces is <em>shapes, not physics</em>, and the distinction matters enough to state
/// plainly. It has no model of any machine. Each channel wanders around its setpoint within the
/// range the profile declares, and channels are independent of one another because nothing here
/// knows whether a temperature should follow a load. Inventing that coupling would be fabricating a
/// relationship — the same defect as a fabricated reading, one level up, and harder to notice
/// because a plausible correlation is exactly what someone would expect to see.
///
/// It is for exercising the ingest path, the charts and the alarms without hardware. Every frame it
/// emits is marked synthetic upstream and stays marked; nothing here is a measurement.
/// </remarks>
public sealed class ProfileSimulatorEngine : ISimulatorEngine, Interfaces.ISimulatedControl
{
    private readonly MonitoringProfile _profile;
    private readonly ConcurrentDictionary<string, double> _setpoints = new(StringComparer.Ordinal);
    private readonly Channel<RawPacket> _channel;
    private readonly TimeSpan _interval;
    private readonly Random _random = new(Seed);

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task _loop = Task.CompletedTask;

    /// <summary>Fixed so two runs of the same profile produce the same shapes.</summary>
    /// <remarks>
    /// A demonstration that looks different every time is hard to talk about, and a test that
    /// depends on it is flaky. Determinism costs nothing here: the data is invented either way.
    /// </remarks>
    private const int Seed = 20260820;

    public ProfileSimulatorEngine(MonitoringProfile profile, double sampleRateHz = 10.0)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (sampleRateHz <= 0 || sampleRateHz > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz), "Sample rate must be between 0 and 1000 Hz.");
        }

        _profile = profile;
        _interval = TimeSpan.FromSeconds(1.0 / sampleRateHz);

        foreach (ProfileChannel channel in profile.Channels)
        {
            _setpoints[channel.Id] = channel.Nominal;
        }

        _channel = Channel.CreateBounded<RawPacket>(new BoundedChannelOptions(4_096)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true
        });
    }

    public bool IsRunning { get; private set; }

    /// <summary>The profile whose channels this engine is producing.</summary>
    public MonitoringProfile Profile => _profile;

    /// <summary>Frames written since the last start.</summary>
    public long FramesGenerated { get; private set; }

    /// <summary>
    /// Node every channel is published under, or the profile id when the profile declares none.
    /// </summary>
    /// <remarks>
    /// The first declared node rather than a round robin. Spreading channels across nodes by
    /// position would attach a quantity to a device for no reason other than its index, and an
    /// operator reading the chart has no way to know the attribution was arbitrary.
    /// </remarks>
    public string NodeId =>
        _profile.Nodes.Count > 0 ? _profile.Nodes[0].Id : Sanitise(_profile.Id);

    /// <summary>Port name stamped on every frame; the node id, since there is no real port.</summary>
    public string PortName => "SIM";

    /// <summary>Moves one channel's setpoint, clamped to the range the profile declares.</summary>
    /// <returns>The value actually applied, which differs when the request was out of range.</returns>
    public double SetSetpoint(string channelId, double value)
    {
        ProfileChannel? channel = _profile.Channels.FirstOrDefault(c => c.Id == channelId);
        if (channel is null) return double.NaN;

        double clamped = Math.Clamp(value, channel.Minimum, channel.Maximum);
        _setpoints[channelId] = clamped;
        return clamped;
    }

    /// <summary>Current setpoint of a channel, or NaN when the profile has no such channel.</summary>
    public double GetSetpoint(string channelId) =>
        _setpoints.TryGetValue(channelId, out double value) ? value : double.NaN;

    /// <summary>
    /// Applies a named scenario's setpoints.
    /// </summary>
    /// <returns>
    /// The channel ids the scenario named that this profile does not declare. Empty means every
    /// setpoint landed.
    /// </returns>
    /// <remarks>
    /// Unknown ids are returned rather than ignored. A scenario that silently sets nothing looks
    /// exactly like one that worked, and the operator's evidence for either is a chart that did not
    /// change — which is also what a correctly applied scenario can look like.
    /// </remarks>
    public IReadOnlyList<string> ApplyScenario(string scenarioId)
    {
        ProfileScenario? scenario = _profile.Scenarios.FirstOrDefault(s => s.Id == scenarioId);
        if (scenario is null) return new[] { scenarioId };

        var unknown = new List<string>();

        foreach ((string channelId, double value) in scenario.Setpoints)
        {
            if (double.IsNaN(SetSetpoint(channelId, value))) unknown.Add(channelId);
        }

        return unknown;
    }

    /// <summary>Returns every channel to the value the profile calls nominal.</summary>
    public void Reset()
    {
        foreach (ProfileChannel channel in _profile.Channels)
        {
            _setpoints[channel.Id] = channel.Nominal;
        }
    }

    /// <summary>Starts generating, and refuses to let two generators overlap.</summary>
    /// <remarks>
    /// The channel below is declared <c>SingleWriter</c>, and that is a promise about the whole
    /// process, not about one loop. A first version started the new loop outright: stop, start,
    /// stop, start in quick succession left the outgoing loop still inside a write while the
    /// incoming one began, which is exactly the case the declaration says cannot happen.
    /// <para>
    /// Waiting for the outgoing loop here would be the obvious fix and the wrong one — this is
    /// called from a button handler on the UI thread, and blocking it for up to one sample interval
    /// freezes the window. The new loop is queued behind the old one instead, so the caller returns
    /// at once and the writes still never overlap.
    /// </para>
    /// </remarks>
    public void StartSimulation()
    {
        lock (_gate)
        {
            if (IsRunning) return;

            CancellationTokenSource? outgoing = _cts;
            Task outgoingLoop = _loop;
            var cts = new CancellationTokenSource();

            _cts = cts;
            FramesGenerated = 0;
            IsRunning = true;

            _loop = outgoingLoop.ContinueWith(
                _ =>
                {
                    outgoing?.Dispose();
                    return GenerateAsync(cts);
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
        }
    }

    public void StopSimulation()
    {
        lock (_gate)
        {
            if (!IsRunning) return;

            IsRunning = false;
            _cts?.Cancel();
        }
    }

    public async IAsyncEnumerable<RawPacket> StreamSimulatedPackets(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (RawPacket packet in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return packet;
        }
    }

    private async Task GenerateAsync(CancellationTokenSource ownCts)
    {
        CancellationToken cancellationToken = ownCts.Token;
        int step = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                DateTime now = DateTime.UtcNow;

                foreach (ProfileChannel channel in _profile.Channels)
                {
                    string frame = Frame(channel, step);
                    if (_channel.Writer.TryWrite(new RawPacket(PortName, frame, now))) FramesGenerated++;
                }

                step++;
                await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Stop requested.
        }
        finally
        {
            // Only if this loop is still the current one. A loop that is shutting down while its
            // replacement is already running would otherwise report the engine as stopped, and the
            // caller's evidence for "started" is this very flag.
            lock (_gate)
            {
                if (ReferenceEquals(_cts, ownCts)) IsRunning = false;
            }
        }
    }

    /// <summary>
    /// One frame in this repository's own <c>$TELE</c> format, checksum included.
    /// </summary>
    /// <remarks>
    /// Emitting real frames rather than packets keeps the simulator on the same road as a device:
    /// the routing rules, the checksum check and the parser all run over synthetic data exactly as
    /// they do over measured data. A simulator that skipped them would leave the parsing path
    /// untested by the only source most installations ever exercise.
    /// </remarks>
    private string Frame(ProfileChannel channel, int step)
    {
        double value = Wander(channel, step);
        string body = string.Create(CultureInfo.InvariantCulture,
            $"TELE,{NodeId},{Sanitise(channel.Id)},{Math.Round(value, Math.Clamp(channel.Decimals, 0, 6))},{channel.Unit}");

        byte checksum = XorChecksum.Calculate(Encoding.UTF8.GetBytes(body));
        return $"${body}*{checksum:X2}";
    }

    /// <summary>
    /// A slow drift plus noise around the setpoint, bounded by the channel's declared range.
    /// </summary>
    /// <remarks>
    /// The amplitude is a fraction of the channel's own range, so a 350-450 V bus moves in volts
    /// and a 0-1 g accelerometer moves in hundredths — without this file knowing what either
    /// quantity is. Channels are given different periods so they do not all peak together, which
    /// would look like a correlation nobody put there.
    /// </remarks>
    private double Wander(ProfileChannel channel, int step)
    {
        double setpoint = _setpoints.TryGetValue(channel.Id, out double s) ? s : channel.Nominal;
        double span = channel.Maximum - channel.Minimum;

        if (span <= 0) return setpoint;

        double period = 40 + StableHash(channel.Id) % 60;
        double drift = span * 0.08 * Math.Sin(2 * Math.PI * step / period);
        double noise = span * 0.01 * (_random.NextDouble() * 2 - 1);

        return Math.Clamp(setpoint + drift + noise, channel.Minimum, channel.Maximum);
    }

    /// <summary>
    /// A hash that is the same in every process, so a channel keeps its period across runs.
    /// </summary>
    /// <remarks>
    /// This was <c>channel.Id.GetHashCode()</c>, and the comment beside it said the period was
    /// "stable across runs". It was not. .NET randomises string hashing per process, so the same
    /// channel drifted with a different period every time the application started — measured here
    /// at 58, then 40, then 40 steps for <c>dab.bus_voltage</c> across three consecutive runs.
    /// <para>
    /// That silently broke the promise <see cref="Seed"/> exists to make. Seeding the noise but not
    /// the drift leaves a demonstration that looks different every time and a waveform no test can
    /// depend on, which is the whole reason determinism was chosen here.
    /// </para>
    /// <para>
    /// FNV-1a, 32-bit, over the UTF-16 code units. Not cryptographic and not meant to be: the only
    /// property needed is that the same string gives the same number on every machine and in every
    /// process.
    /// </para>
    /// </remarks>
    private static uint StableHash(string value)
    {
        const uint OffsetBasis = 2166136261;
        const uint Prime = 16777619;

        uint hash = OffsetBasis;
        foreach (char c in value ?? string.Empty)
        {
            hash = (hash ^ (byte)(c & 0xFF)) * Prime;
            hash = (hash ^ (byte)(c >> 8)) * Prime;
        }

        return hash;
    }

    /// <summary>Strips the delimiters the frame format uses, so an id cannot break the frame.</summary>
    private static string Sanitise(string value)
    {
        string trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0) return "unnamed";

        var builder = new StringBuilder(trimmed.Length);
        foreach (char c in trimmed)
        {
            builder.Append(c is ',' or '*' or '$' or '\r' or '\n' ? '_' : c);
        }

        return builder.ToString();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        StopSimulation();
        _channel.Writer.TryComplete();

        Task loop;
        lock (_gate) { loop = _loop; }

        try { await loop.ConfigureAwait(false); } catch (OperationCanceledException) { }

        lock (_gate) { _cts?.Dispose(); _cts = null; }
    }
}
