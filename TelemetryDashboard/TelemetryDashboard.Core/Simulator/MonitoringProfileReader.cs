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

        return new MonitoringProfile
        {
            Id = dto.Id!,
            DisplayName = dto.DisplayName!,
            Summary = dto.Summary ?? string.Empty,
            Nodes = nodes,
            Channels = channels,
            Scenarios = scenarios
        };
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
                Decimals = Math.Clamp(channel.Decimals, 0, 4)
            });
        }

        return channels;
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
