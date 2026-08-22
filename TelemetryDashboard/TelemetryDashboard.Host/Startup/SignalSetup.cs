using System;
using System.Collections.Generic;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Host.Ingest;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Drives declared channels with known waveforms, so the analysis can be checked against them.
/// </summary>
/// <remarks>
/// <see cref="SignalGeneratorService"/> had been written, tested and constructed by nothing. What
/// its absence cost is not a missing feature but a missing <em>reference</em>: the simulator emits
/// one shape per channel at a period derived from a hash, so nothing in this product could tell an
/// operator whether the peak <c>/api/spectrum</c> draws is the frequency the channel is actually
/// oscillating at. The evidence was that the number looked plausible.
/// </remarks>
public static class SignalSetup
{
    /// <param name="Applied">Signals now driving a channel.</param>
    /// <param name="Problems">Declarations that were refused, each with the reason.</param>
    public readonly record struct Result(
        IReadOnlyList<InjectedSignal> Applied, IReadOnlyList<string> Problems);

    /// <summary>Applies every declared signal to the engine behind <paramref name="source"/>.</summary>
    public static Result Apply(HostOptions options, ITelemetrySource? source)
    {
        ArgumentNullException.ThrowIfNull(options);

        var applied = new List<InjectedSignal>();
        var problems = new List<string>();

        if (options.Signals.Count == 0) return new Result(applied, problems);

        ProfileSimulatorEngine? engine = EngineOf(source);
        if (engine is null)
        {
            problems.Add("no generated source is running, so there is nothing to drive");
            return new Result(applied, problems);
        }

        foreach (string declaration in options.Signals)
        {
            InjectedSignal signal;
            try
            {
                signal = InjectedSignal.Parse(declaration);
            }
            catch (FormatException ex)
            {
                problems.Add($"'{declaration}': {ex.Message}");
                continue;
            }

            // Nyquist is a property of the running engine, so it cannot be checked at the command
            // line. Above it the sampled waveform is indistinguishable from a slower one, and the
            // spectrum would report a peak that is real, wrong and symptomless -- which is the one
            // failure a reference signal must not have.
            if (signal.AliasesAt(engine.SampleRateHz))
            {
                problems.Add(
                    $"'{declaration}': {signal.FrequencyHz:G6} Hz is above the "
                    + $"{engine.SampleRateHz / 2:G6} Hz Nyquist limit of this simulator "
                    + $"({engine.SampleRateHz:G6} Hz per channel). It would fold back and be "
                    + "reported as a lower frequency that is not there.");
                continue;
            }

            if (!engine.InjectSignal(signal))
            {
                problems.Add($"'{declaration}': this profile declares no channel '{signal.Channel}'");
                continue;
            }

            applied.Add(signal);
        }

        return new Result(applied, problems);
    }

    /// <summary>Banner lines describing what is being driven, or nothing when none is.</summary>
    public static IEnumerable<string> BannerLines(Result resolved)
    {
        foreach (string problem in resolved.Problems) yield return $"  signals       ! {problem}";

        if (resolved.Applied.Count == 0) yield break;

        yield return $"  signals       {resolved.Applied.Count} channel(s) driven by a known waveform";

        foreach (InjectedSignal signal in resolved.Applied)
        {
            yield return $"                {signal.Channel} = {signal.Shape.ToString().ToLowerInvariant()} "
                       + $"@ {signal.FrequencyHz:G6} Hz, ±{signal.Amplitude:G6} about its setpoint";
        }

        yield return "                these channels are a reference, not a simulation of the machine";
    }

    private static ProfileSimulatorEngine? EngineOf(ITelemetrySource? source) => source switch
    {
        SimulatedTelemetrySource simulated => simulated.Control as ProfileSimulatorEngine,
        LoopbackTelemetrySource loopback => loopback.Control as ProfileSimulatorEngine,
        _ => null
    };
}
