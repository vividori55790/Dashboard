using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// What <c>/api/control</c> reports, separated from what it does.
/// </summary>
/// <remarks>
/// The reply carries both what was asked for and what happened, because on a commissioning run
/// those differ in the one way that matters: a value the profile would not admit is applied
/// clamped, and a caller told only "Success" would believe the machine went where they sent it.
/// </remarks>
public static partial class ControlEndpoint
{
    public sealed record ChannelState
    {
        public string Id { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
        public double Minimum { get; init; }
        public double Maximum { get; init; }
        public double Nominal { get; init; }

        /// <summary>The setpoint in force.</summary>
        public double Setpoint { get; init; }
    }

    public sealed record ScenarioState
    {
        public string Id { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
    }

    public sealed record Result
    {
        public string Status { get; init; } = "Success";
        public string? Reason { get; init; }

        /// <summary>What was asked for, echoed so a reply cannot be read against the wrong request.</summary>
        public string? Command { get; init; }
        public string? Channel { get; init; }

        /// <summary>The value the caller asked for, when they asked for one.</summary>
        public double? Requested { get; init; }

        /// <summary>The value actually in force afterwards.</summary>
        public double? Applied { get; init; }

        /// <summary>
        /// True when <see cref="Applied"/> differs from <see cref="Requested"/> because the profile's
        /// range would not admit it.
        /// </summary>
        /// <remarks>
        /// Stated rather than left to be noticed. A caller who asks for 999 V, gets 450 and is told
        /// "Success" will believe the bus is at 999 — and on a commissioning run that belief is the
        /// difference between "the alarm did not fire" and "the alarm was never given the chance".
        /// </remarks>
        public bool Clamped { get; init; }

        /// <summary>Channel ids a scenario named that this profile does not declare.</summary>
        public IReadOnlyList<string> Unknown { get; init; } = Array.Empty<string>();

        public IReadOnlyList<ChannelState> Channels { get; init; } = Array.Empty<ChannelState>();
        public IReadOnlyList<ScenarioState> Scenarios { get; init; } = Array.Empty<ScenarioState>();
    }
}
