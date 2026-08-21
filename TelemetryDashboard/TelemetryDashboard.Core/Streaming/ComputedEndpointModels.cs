using System;
using System.Collections.Generic;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// What <c>/api/computed</c> answers with.
/// </summary>
/// <remarks>
/// Separated from the evaluation so the shape a client depends on can be read on its own. Every
/// field here exists to stop a derived number being read as a measurement: the expression it came
/// from, both names of every input, how each input was aligned, and the reason when there is no
/// value at all.
/// </remarks>
public static partial class ComputedEndpoint
{
    /// <summary>One expression's answer at the requested instant.</summary>
    public sealed record ComputedValue
    {
        public string Id { get; init; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
        public string Expression { get; init; } = string.Empty;

        /// <summary>Always true, and serialised on every row rather than documented.</summary>
        /// <remarks>
        /// A client that merges these into a chart of measurements has to be able to tell them
        /// apart at the point of use, and the point of use is a JSON row rather than this comment.
        /// </remarks>
        public bool Derived { get; init; } = true;

        /// <summary><c>Computed</c> or <c>Unavailable</c>.</summary>
        public string Status { get; init; } = "Computed";

        /// <summary>Why there is no value, naming the input that was missing.</summary>
        public string? Reason { get; init; }

        public double? Value { get; init; }

        public IReadOnlyList<ComputedInput> Inputs { get; init; } = Array.Empty<ComputedInput>();
    }

    /// <summary>One input of an expression: what it was called, what it resolved to, what it read.</summary>
    /// <remarks>
    /// Both names are carried because they are usually different and the difference is where the
    /// mistakes live. An expression says <c>dab.bus_voltage</c>; the series it actually read is
    /// <c>SIM:COM3.dab.bus_voltage</c>, and an operator debugging a channel that will not compute
    /// needs to see which series the host picked, not only which name they typed.
    /// </remarks>
    public sealed record ComputedInput
    {
        /// <summary>The name as written in the expression.</summary>
        public string Declared { get; init; } = string.Empty;

        /// <summary>The series key it resolved to, or null when it resolved to none.</summary>
        public string? Resolved { get; init; }

        public double? Value { get; init; }
        public string Kind { get; init; } = nameof(AlignmentKind.None);
        public bool AnswersTheInstant { get; init; }
        public double GapSec { get; init; }
        public int Samples { get; init; }

        /// <summary>Why this input could not be used. Null when it was.</summary>
        public string? Reason { get; init; }
    }

    public sealed record Result
    {
        public string Status { get; init; } = "Success";
        public string? Reason { get; init; }

        public double AtSec { get; init; }
        public double WindowSec { get; init; }

        /// <summary>Expressions declared on this host.</summary>
        public int Declared { get; init; }

        /// <summary>Of those, how many produced a value at this instant.</summary>
        public int Available { get; init; }

        public IReadOnlyList<ComputedValue> Channels { get; init; } = Array.Empty<ComputedValue>();
    }

}
