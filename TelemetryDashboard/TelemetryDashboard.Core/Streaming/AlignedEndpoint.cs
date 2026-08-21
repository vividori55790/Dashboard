using System;
using System.Collections.Generic;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Query;
using TelemetryDashboard.Core.Services;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// Answers <c>/api/aligned</c>: several channels as they stood at one instant.
/// </summary>
/// <remarks>
/// Channels do not arrive together. Each sample lands when its device sent it, so "what were the
/// input and the output at the same moment" — the question behind every efficiency, every ratio and
/// every phase relationship — has no answer in the raw stream. Reading the latest of each is the
/// obvious thing to do and is wrong by exactly the interval between them.
/// <para>
/// <see cref="TimeSyncJitterBuffer"/> has been able to answer it since M1 and was constructed by
/// nothing, so Feature 2 could not be reached from any running program. It is used here as it
/// stands, over a window of the series store, so no second copy of the stream is kept.
/// </para>
/// <para>
/// Every answer says how it was obtained. An interpolated value is a value nothing reported, and a
/// held one describes a different instant entirely; a caller that cannot tell those from a
/// measurement will publish all three as measurements.
/// </para>
/// </remarks>
public static class AlignedEndpoint
{
    /// <summary>How much history to consider around the requested instant.</summary>
    /// <remarks>
    /// Wide enough to bracket the instant even for a slow channel, and bounded so a request cannot
    /// walk the whole buffer for every channel it names.
    /// </remarks>
    public const double DefaultWindowSec = 30.0;

    /// <summary>One channel's answer.</summary>
    public sealed record ChannelAlignment
    {
        public string Channel { get; init; } = string.Empty;

        /// <summary>The value, or null when nothing could be aligned.</summary>
        public double? Value { get; init; }

        /// <summary>Exact, Interpolated, HeldBefore, HeldAfter or None.</summary>
        public string Kind { get; init; } = nameof(AlignmentKind.None);

        /// <summary>Whether this describes the instant that was asked about.</summary>
        public bool AnswersTheInstant { get; init; }

        /// <summary>For a held value, how far outside the samples the instant lies.</summary>
        public double GapSec { get; init; }

        /// <summary>Samples the alignment had to work with.</summary>
        public int Samples { get; init; }
    }

    /// <summary>The whole answer.</summary>
    public sealed record Result
    {
        public string Status { get; init; } = "Success";
        public string? Reason { get; init; }

        /// <summary>The instant every channel was aligned to, in Unix seconds.</summary>
        public double AtSec { get; init; }

        public double WindowSec { get; init; }

        /// <summary>How many channels answered for the instant rather than near it.</summary>
        /// <remarks>
        /// Stated so a caller can reject a whole reading at once. A ratio computed from one
        /// interpolated value and one held from four seconds ago is not a ratio of anything.
        /// </remarks>
        public int AnsweredTheInstant { get; init; }

        public IReadOnlyList<ChannelAlignment> Channels { get; init; } = Array.Empty<ChannelAlignment>();
    }

    /// <summary>Aligns every named channel to <paramref name="atSec"/>.</summary>
    public static Result Compute(
        SeriesStore store, IReadOnlyList<string> channels, double atSec, double windowSec)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(channels);

        if (channels.Count == 0)
        {
            return new Result
            {
                Status = "Error",
                Reason = "no channels named; pass ?channels=a,b,c",
                AtSec = atSec
            };
        }

        double span = windowSec > 0 ? windowSec : DefaultWindowSec;
        var buffer = new TimeSyncJitterBuffer();
        var answers = new List<ChannelAlignment>(channels.Count);
        int answered = 0;

        foreach (string channel in channels)
        {
            ChannelSeriesBuffer? series = store.Find(channel);
            int taken = 0;

            if (series is not null)
            {
                var points = new SeriesPoint[series.Count];
                taken = series.CopyWindow(atSec - span, atSec + span, points);

                for (int i = 0; i < taken; i++)
                {
                    buffer.EnqueueSample(channel, points[i].TimestampSec, points[i].Value);
                }
            }

            AlignedSample aligned = buffer.GetAligned(channel, atSec);
            if (aligned.AnswersTheInstant) answered++;

            answers.Add(new ChannelAlignment
            {
                Channel = channel,
                Value = aligned.HasValue ? aligned.Value : null,
                Kind = aligned.Kind.ToString(),
                AnswersTheInstant = aligned.AnswersTheInstant,
                GapSec = aligned.GapSec,
                Samples = taken
            });
        }

        return new Result
        {
            AtSec = atSec,
            WindowSec = span,
            AnsweredTheInstant = answered,
            Channels = answers
        };
    }
}
