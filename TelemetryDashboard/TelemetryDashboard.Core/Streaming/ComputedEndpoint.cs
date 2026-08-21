using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Query;
using TelemetryDashboard.Core.Services;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// Answers <c>/api/computed</c>: quantities nothing measures, from channels that do.
/// </summary>
/// <remarks>
/// Efficiency is the number a converter is judged by and no converter reports it. Neither does it
/// report either power: both sides are a voltage and a current, on separate channels, from separate
/// MCUs, arriving at separate instants. Until now the product could show all four and could not
/// show the one figure they exist to produce.
/// <para>
/// Each expression is evaluated over <see cref="AlignedEndpoint"/> rather than over the latest
/// value of each input, because multiplying a voltage from now by a current from 300 ms ago gives a
/// power that was never drawn. Alignment is the difference between a computed channel and a
/// plausible one.
/// </para>
/// <para>
/// An answer is withheld unless every input <em>answers the instant</em> — measured there, or
/// interpolated between two samples that bracket it. A held value is the last thing a channel said
/// before it went quiet, and a converter whose current sensor stopped reporting has an unknown
/// efficiency, not the efficiency it had when the sensor was last heard from.
/// </para>
/// </remarks>
public static partial class ComputedEndpoint
{
    /// <summary>Evaluates <paramref name="declared"/> at <paramref name="atSec"/>.</summary>
    /// <param name="only">
    /// When given, restricts the answer to these ids. An id that was not declared is reported as
    /// such rather than omitted, so a client cannot mistake a typo for a channel that went quiet.
    /// </param>
    public static Result Compute(
        SeriesStore store,
        IReadOnlyList<ComputedChannel> declared,
        double atSec,
        double windowSec,
        IReadOnlyList<string>? only = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        declared ??= Array.Empty<ComputedChannel>();

        if (declared.Count == 0)
        {
            return new Result
            {
                Status = "Error",
                Reason = "this host declares no computed channels; " +
                         "start it with --computed \"id[unit] = expression\" to add one",
                AtSec = atSec,
                WindowSec = windowSec
            };
        }

        IEnumerable<ComputedChannel> wanted = only is { Count: > 0 }
            ? declared.Where(c => only.Contains(c.Id, StringComparer.Ordinal))
            : declared;

        var answers = new List<ComputedValue>();
        int available = 0;

        foreach (ComputedChannel channel in wanted)
        {
            ComputedValue answer = Answer(store, channel, atSec, windowSec);
            if (answer.Value is not null) available++;
            answers.Add(answer);
        }

        if (only is { Count: > 0 })
        {
            foreach (string id in only.Where(i => !declared.Any(d => d.Id == i)))
            {
                answers.Add(new ComputedValue
                {
                    Id = id,
                    Status = "Unavailable",
                    Reason = "no computed channel is declared under this id on this host"
                });
            }
        }

        return new Result
        {
            AtSec = atSec,
            WindowSec = windowSec,
            Declared = declared.Count,
            Available = available,
            Channels = answers
        };
    }

    private static ComputedValue Answer(SeriesStore store, ComputedChannel channel, double atSec, double windowSec)
    {
        var basis = new ComputedValue
        {
            Id = channel.Id,
            Unit = channel.Unit,
            Expression = channel.Expression
        };

        // Names first. An expression over a channel this host has never heard of cannot be aligned,
        // and the reason it cannot -- a typo, or a node that has not reported -- is more useful than
        // an alignment result full of empty series.
        var resolutions = channel.Inputs
            .Select(input => (Declared: input, Resolved: ComputedInputResolver.Resolve(store, input)))
            .ToList();

        if (resolutions.Any(r => r.Resolved.Key is null))
        {
            return basis with
            {
                Status = "Unavailable",
                Reason = resolutions.First(r => r.Resolved.Key is null).Resolved.Reason,
                Inputs = resolutions.Select(r => new ComputedInput
                {
                    Declared = r.Declared,
                    Resolved = r.Resolved.Key,
                    Reason = r.Resolved.Reason
                }).ToList()
            };
        }

        string[] keys = resolutions.Select(r => r.Resolved.Key!).ToArray();
        AlignedEndpoint.Result aligned = AlignedEndpoint.Compute(store, keys, atSec, windowSec);

        var inputs = new List<ComputedInput>(keys.Length);
        var byDeclaredName = new Dictionary<string, double?>(StringComparer.Ordinal);

        for (int i = 0; i < keys.Length; i++)
        {
            AlignedEndpoint.ChannelAlignment a = aligned.Channels[i];
            byDeclaredName[resolutions[i].Declared] = a.AnswersTheInstant ? a.Value : null;

            inputs.Add(new ComputedInput
            {
                Declared = resolutions[i].Declared,
                Resolved = keys[i],
                Value = a.Value,
                Kind = a.Kind,
                AnswersTheInstant = a.AnswersTheInstant,
                GapSec = a.GapSec,
                Samples = a.Samples,
                Reason = a.AnswersTheInstant ? null : Explain(resolutions[i].Declared, a)
            });
        }

        basis = basis with { Inputs = inputs };

        ComputedInput? unusable = inputs.FirstOrDefault(i => !i.AnswersTheInstant);
        if (unusable is not null)
        {
            return basis with { Status = "Unavailable", Reason = unusable.Reason };
        }

        double? value = channel.Evaluate(id => byDeclaredName.TryGetValue(id, out double? v) ? v : null);

        return value is null
            ? basis with
            {
                Status = "Unavailable",
                Reason = "every input answered this instant and the expression still has no value — " +
                         "a division by zero, or a root of a negative"
            }
            : basis with { Value = value };
    }

    private static string Explain(string declared, AlignedEndpoint.ChannelAlignment input) =>
        input.Samples == 0
            ? $"'{declared}' has reported nothing in this window"
            : string.Create(CultureInfo.InvariantCulture,
                $"'{declared}' has no reading at this instant; the nearest is {input.GapSec:F2}s away " +
                $"({input.Kind}), and holding it would describe a different moment");
}
