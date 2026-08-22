using System;
using System.Collections.Generic;
using System.Linq;

namespace TelemetryDashboard.Core.Simulator;

/// <summary>
/// The JSON shapes a profile file is written in, and the validation that turns one into a profile.
/// </summary>
/// <remarks>
/// Deserialisation targets are kept separate from <see cref="MonitoringProfile"/> so a file with a
/// missing label or an inverted range produces a sentence an operator can act on, rather than a
/// half-built profile that only misbehaves once somebody drags a slider. Every rejected entry is
/// named: a profile that silently fails to appear is worse than one that never loaded.
/// </remarks>
internal static class MonitoringProfileReader
{
    internal static MonitoringProfile? Convert(
        ProfileDto dto, List<MonitoringProfile> existing, List<string> problems)
    {
        string name = string.IsNullOrWhiteSpace(dto.Id) ? "(id 없음)" : dto.Id!;

        if (string.IsNullOrWhiteSpace(dto.Id) || string.IsNullOrWhiteSpace(dto.DisplayName))
        {
            problems.Add($"'{name}' — id 와 displayName 이 모두 필요합니다.");
            return null;
        }

        if (existing.Any(p => string.Equals(p.Id, dto.Id, StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add($"'{name}' — 이미 있는 id 라 건너뛰었습니다.");
            return null;
        }

        if (dto.Channels is not { Count: > 0 })
        {
            problems.Add($"'{name}' — 채널이 하나도 없습니다.");
            return null;
        }

        List<ProfileNode>? nodes = ReadNodes(dto, name, problems);
        if (nodes is null) return null;

        List<ProfileChannel>? channels = ReadChannels(dto, name, problems);
        if (channels is null) return null;

        List<ProfileScenario>? scenarios = ReadScenarios(dto, name, channels, problems);
        if (scenarios is null) return null;

        List<string>? computed = ReadComputed(dto, name, channels, problems);
        if (computed is null) return null;

        List<string>? limits = ReadLimits(dto, name, problems);
        if (limits is null) return null;

        return new MonitoringProfile
        {
            Id = dto.Id!,
            DisplayName = dto.DisplayName!,
            Summary = dto.Summary ?? string.Empty,
            Nodes = nodes,
            Channels = channels,
            Scenarios = scenarios,
            Computed = computed,
            Limits = limits
        };
    }

    /// <summary>
    /// Reads the engineering limits, checking each one parses.
    /// </summary>
    /// <remarks>
    /// The channel is deliberately <em>not</em> checked against the profile's own channel list, as
    /// computed inputs are. A limit legitimately names a derived channel the profile computes, or a
    /// quantity a device reports that no profile declares, and refusing those would force the
    /// author to describe every channel before being allowed to protect it. What the profile can
    /// say honestly is whether the rule parses; <c>/api/limits</c> reports the ones nothing ever
    /// matched, which is where a misspelling is actually visible.
    /// </remarks>
    private static List<string>? ReadLimits(ProfileDto dto, string name, List<string> problems)
    {
        var accepted = new List<string>();
        if (dto.Limits is not { Count: > 0 }) return accepted;

        foreach (string declaration in dto.Limits)
        {
            try
            {
                Analytics.ChannelLimit.Parse(declaration);
                accepted.Add(declaration);
            }
            catch (FormatException ex)
            {
                problems.Add($"'{name}' — 한계값 '{declaration}': {ex.Message}");
            }
        }

        return accepted;
    }

    /// <summary>
    /// Reads the derived quantities, checking each one parses and reads channels that exist.
    /// </summary>
    /// <remarks>
    /// Both checks happen here rather than at the first request, because a computed channel that
    /// names a misspelled input is permanently unavailable and there is no later moment at which
    /// anyone finds that out: the endpoint would answer "that input has reported nothing", which is
    /// exactly what it says about a sensor that has genuinely gone quiet.
    /// </remarks>
    private static List<string>? ReadComputed(
        ProfileDto dto, string name, List<ProfileChannel> channels, List<string> problems)
    {
        var accepted = new List<string>();
        if (dto.Computed is not { Count: > 0 }) return accepted;

        var known = new HashSet<string>(channels.Select(c => c.Id), StringComparer.Ordinal);

        foreach (string declaration in dto.Computed)
        {
            Analytics.ComputedChannel parsed;
            try
            {
                parsed = Analytics.ComputedChannel.Parse(declaration);
            }
            catch (FormatException ex)
            {
                problems.Add($"'{name}' — 계산 채널 '{declaration}': {ex.Message}");
                continue;
            }

            string[] missing = parsed.Inputs.Where(i => !known.Contains(i)).ToArray();
            if (missing.Length > 0)
            {
                problems.Add(
                    $"'{name}' — 계산 채널 '{parsed.Id}' 이(가) 이 프로파일에 없는 채널을 읽습니다: " +
                    string.Join(", ", missing));
                continue;
            }

            accepted.Add(declaration);
        }

        return accepted;
    }

    /// <summary>
    /// Reads the devices a profile declares. An absent list means none, which is allowed.
    /// </summary>
    /// <remarks>
    /// A duplicate id is rejected rather than merged: the id is what goes out in the power command,
    /// so two buttons carrying the same one would send the same instruction under two captions and
    /// leave the operator with no way to tell which device answered.
    /// </remarks>
    private static List<ProfileNode>? ReadNodes(ProfileDto dto, string name, List<string> problems)
    {
        var nodes = new List<ProfileNode>();

        foreach (NodeDto node in dto.Nodes ?? [])
        {
            if (string.IsNullOrWhiteSpace(node.Id) || string.IsNullOrWhiteSpace(node.Label))
            {
                problems.Add($"'{name}' — 노드에 id 또는 label 이 없습니다.");
                return null;
            }

            if (nodes.Any(n => string.Equals(n.Id, node.Id, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"'{name}' — 노드 id '{node.Id}' 가 중복입니다.");
                return null;
            }

            nodes.Add(new ProfileNode
            {
                Id = node.Id!,
                Label = node.Label!,
                Description = node.Description ?? string.Empty
            });
        }

        return nodes;
    }

    private static List<ProfileChannel>? ReadChannels(ProfileDto dto, string name, List<string> problems)
    {
        var channels = new List<ProfileChannel>(dto.Channels!.Count);

        foreach (ChannelDto channel in dto.Channels)
        {
            if (string.IsNullOrWhiteSpace(channel.Id) || string.IsNullOrWhiteSpace(channel.Label))
            {
                problems.Add($"'{name}' — 채널에 id 또는 label 이 없습니다.");
                return null;
            }

            if (channel.Maximum <= channel.Minimum)
            {
                problems.Add($"'{name}' 채널 '{channel.Id}' — maximum 이 minimum 보다 커야 합니다.");
                return null;
            }

            channels.Add(new ProfileChannel
            {
                Id = channel.Id!,
                Label = channel.Label!,
                Unit = channel.Unit ?? string.Empty,
                Minimum = channel.Minimum,
                Maximum = channel.Maximum,
                Nominal = Math.Clamp(channel.Nominal, channel.Minimum, channel.Maximum),
                Decimals = Math.Clamp(channel.Decimals, 0, 4),
                Integrates = string.IsNullOrWhiteSpace(channel.Integrates)
                    ? null
                    : new ChannelIntegration
                    {
                        Source = channel.Integrates!,
                        PerSecond = channel.IntegralPerSecond
                    }
            });
        }

        return ValidateIntegrations(channels, name, problems) ? channels : null;
    }

    /// <summary>
    /// Checks every accumulating channel after the whole list is known.
    /// </summary>
    /// <remarks>
    /// After, rather than inside the loop, because a channel is allowed to accumulate one declared
    /// further down the file — the order channels are written in is the author's business.
    /// <para>
    /// A rate of zero is refused rather than accepted as "does not move". A channel that declares
    /// itself an integral and then never changes is the failure this whole mechanism exists to
    /// avoid: it holds still at its nominal and reads as a healthy measurement.
    /// </para>
    /// </remarks>
    private static bool ValidateIntegrations(
        List<ProfileChannel> channels, string name, List<string> problems)
    {
        foreach (ProfileChannel channel in channels)
        {
            if (channel.Integrates is not { } integration) continue;

            if (!channels.Any(c => string.Equals(c.Id, integration.Source, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add(
                    $"'{name}' 채널 '{channel.Id}' — 없는 채널 '{integration.Source}' 을 적분하려 합니다.");
                return false;
            }

            if (string.Equals(channel.Id, integration.Source, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"'{name}' 채널 '{channel.Id}' — 자기 자신을 적분할 수 없습니다.");
                return false;
            }

            if (!double.IsFinite(integration.PerSecond) || integration.PerSecond == 0)
            {
                problems.Add(
                    $"'{name}' 채널 '{channel.Id}' — integralPerSecond 가 0 이거나 숫자가 아닙니다. " +
                    "적분한다고 선언하고 움직이지 않는 채널은 정상값처럼 보입니다.");
                return false;
            }
        }

        return true;
    }

    private static List<ProfileScenario>? ReadScenarios(
        ProfileDto dto, string name, List<ProfileChannel> channels, List<string> problems)
    {
        var scenarios = new List<ProfileScenario>();

        foreach (ScenarioDto scenario in dto.Scenarios ?? [])
        {
            if (string.IsNullOrWhiteSpace(scenario.Id) || string.IsNullOrWhiteSpace(scenario.Label))
            {
                problems.Add($"'{name}' — 시나리오에 id 또는 label 이 없습니다.");
                return null;
            }

            string? unknown = scenario.Setpoints?.Keys.FirstOrDefault(
                key => !channels.Any(c => string.Equals(c.Id, key, StringComparison.OrdinalIgnoreCase)));

            if (unknown is not null)
            {
                problems.Add($"'{name}' 시나리오 '{scenario.Id}' — 없는 채널 '{unknown}' 을 지정했습니다.");
                return null;
            }

            scenarios.Add(new ProfileScenario
            {
                Id = scenario.Id!,
                Label = scenario.Label!,
                Description = scenario.Description ?? string.Empty,
                Fault = scenario.Fault,
                Setpoints = scenario.Setpoints ?? new Dictionary<string, double>()
            });
        }

        return scenarios;
    }
}
