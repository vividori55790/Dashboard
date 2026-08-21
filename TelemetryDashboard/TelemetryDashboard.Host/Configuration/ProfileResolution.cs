using System;
using System.Linq;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Host.Configuration;

/// <summary>
/// Resolves <c>--profile</c> to a profile, or explains why it could not.
/// </summary>
/// <remarks>
/// Two things in this host are decided by the profile — what the simulator generates and what an
/// exported dashboard draws — and they have to reach the same answer. Resolving it twice would
/// leave a run whose page describes one machine while its stream carries another, which is the
/// class of disagreement this whole area of the codebase has been spent removing.
/// </remarks>
public static class ProfileResolution
{
    /// <summary>What a resolution attempt produced: a profile, a complaint, or both.</summary>
    /// <param name="Profile">The profile to use, or null when the named one does not exist.</param>
    /// <param name="Warning">A parse failure worth reporting, even when a profile was resolved.</param>
    /// <param name="Error">Why no profile could be resolved. Null when <paramref name="Profile"/> is set.</param>
    public readonly record struct Result(MonitoringProfile? Profile, string? Warning, string? Error);

    /// <summary>Loads the profile set and picks the requested profile, or the first available.</summary>
    /// <remarks>
    /// An unknown id is refused and the available ids are listed. Falling back would generate — or
    /// draw — a different machine's channels under the name the operator chose, which is precisely
    /// what profiles exist to stop.
    /// </remarks>
    public static Result Resolve(string? profileId, string baseDirectory)
    {
        MonitoringProfileSet profiles = MonitoringProfileStore.Load(baseDirectory);

        // A profile file that failed to parse and one that was never written produce the same set
        // of profiles, and only the first is a mistake somebody needs to hear about.
        string? warning = profiles.Status == ProfileSourceStatus.Invalid ? profiles.Message : null;

        if (profileId is null)
        {
            return new Result(profiles.Profiles.FirstOrDefault(), warning, null);
        }

        MonitoringProfile? named = profiles.Profiles
            .FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));

        if (named is not null) return new Result(named, warning, null);

        return new Result(
            null,
            warning,
            $"no profile with id '{profileId}'. Available: "
            + string.Join(", ", profiles.Profiles.Select(p => p.Id)));
    }
}
