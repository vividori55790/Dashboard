using System;

namespace TelemetryDashboard.Core.Analytics.Detectors;

/// <summary>
/// One detector's flagged verdict about one sample, retained so it can be looked at afterwards.
/// </summary>
/// <remarks>
/// The sample and its time travel with the verdict rather than being looked up later. A retained
/// score whose value and timestamp have to be recovered from somewhere else is a number an operator
/// cannot check, and the channel it came from may well have scrolled out of every buffer by the
/// time anybody asks.
/// <para>
/// <see cref="DetectorVerdict.DetectorId"/> inside is what makes a disputed flag traceable to the
/// detector and settings that produced it, which is the same guarantee
/// <see cref="AnomalyResult.AnalyzerId"/> gives for the built-in engine.
/// </para>
/// </remarks>
/// <param name="Channel">The channel it was flagged on.</param>
/// <param name="Value">The sample that was flagged.</param>
/// <param name="ObservedUtc">When that sample was observed.</param>
/// <param name="Verdict">The verdict, carrying the detector id that produced it.</param>
public sealed record FlaggedVerdict(string Channel, double Value, DateTime ObservedUtc, DetectorVerdict Verdict);
