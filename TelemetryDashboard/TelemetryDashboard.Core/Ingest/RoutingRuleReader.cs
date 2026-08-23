using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Ingest;

/// <summary>
/// Reads the rules that say what a device on this bench is actually sending.
/// </summary>
/// <remarks>
/// Until this existed the only rules any front end registered were the hardcoded defaults, which
/// recognise the framing this product's own generated firmware emits. That is exactly the firmware
/// a real installation does not have: a bench STM32 says <c>Vout</c> in millivolts, and every band,
/// computed channel and twin placement the profile declares against <c>psfb.output_voltage</c> in
/// volts matched nothing. The readings arrived and charted themselves; nothing judged them.
/// <para>
/// Loud about what it cannot honour, for the reason <see cref="JsonChannelMapReader"/> is: a
/// half-read map is a rig half-monitored, and the half that is missing has no symptom.
/// </para>
/// </remarks>
public static class RoutingRuleReader
{
    /// <param name="Rules">Rules that could be read, in file order.</param>
    /// <param name="Warnings">Clauses that were skipped, each with the reason.</param>
    public readonly record struct Result(IReadOnlyList<RoutingRule> Rules, IReadOnlyList<string> Warnings);

    /// <summary>Reads <paramref name="path"/>, or explains why it cannot be used.</summary>
    /// <exception cref="FileNotFoundException">There is no such file.</exception>
    /// <exception cref="InvalidDataException">The file is not readable as a rule set.</exception>
    public static Result Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path)) throw new FileNotFoundException($"no rule file at {path}", path);

        return Parse(File.ReadAllText(path, Services.Utf8Files.WithoutBom), Path.GetFileName(path));
    }

    /// <summary>Reads a rule set from <paramref name="json"/>.</summary>
    public static Result Parse(string json, string origin = "rules")
    {
        RoutingRuleFile? file;
        try
        {
            file = JsonSerializer.Deserialize<RoutingRuleFile>(
                json,
                new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    // A hand-edited file with a comma after its last entry is a file operators
                    // produce, and it is the shape the drafted file leaves behind when a commented
                    // mapping at the end is uncommented. Refusing it teaches nothing.
                    AllowTrailingCommas = true
                });
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{origin} is not readable JSON: {ex.Message}", ex);
        }

        if (file?.Rules is not { Count: > 0 })
        {
            // Not a warning. A file naming no rules changes nothing while looking like
            // configuration, and somebody who wrote one believes their device is mapped.
            throw new InvalidDataException($"{origin} declares no rules, so it would change nothing.");
        }

        var rules = new List<RoutingRule>();
        var warnings = new List<string>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < file.Rules.Count; i++)
        {
            if (Read(file.Rules[i], i, warnings) is not { } rule) continue;

            // Two rules that match the same frames are not two configurations, they are one
            // ambiguity: the router holds its rules in a dictionary and iterates it in whatever
            // order it likes, so which one won could differ between two runs of the same build.
            string claim = $"{rule.RuleType}|{rule.Tag}|{rule.Port}";
            if (!claimed.Add(claim))
            {
                warnings.Add(
                    $"rule {i + 1}: a {rule.RuleType} rule for tag '{rule.Tag}' on port "
                    + $"{rule.Port} was already declared, and which of the two would be applied is "
                    + "not decided anywhere. Merge their channels into one rule.");
                continue;
            }

            rules.Add(rule);
        }

        if (rules.Count == 0)
        {
            throw new InvalidDataException(
                $"{origin} has {file.Rules.Count} rule(s) and none of them could be read: "
                + string.Join("; ", warnings));
        }

        return new Result(rules, warnings);
    }

    private static RoutingRule? Read(RuleDto dto, int index, List<string> warnings)
    {
        string where = $"rule {index + 1}";

        if (!Enum.TryParse(dto.Type ?? "prefix", ignoreCase: true, out RuleType type))
        {
            warnings.Add($"{where}: '{dto.Type}' is not a rule type (prefix, json or columns).");
            return null;
        }

        if (type == RuleType.Prefix && string.IsNullOrWhiteSpace(dto.Tag))
        {
            warnings.Add($"{where}: a prefix rule needs a tag, e.g. \"tag\": \"TELE\".");
            return null;
        }

        var rule = new RoutingRule
        {
            Id = $"file-{index + 1}",
            RuleType = type,
            Tag = (dto.Tag ?? string.Empty).TrimStart('$'),
            Port = string.IsNullOrWhiteSpace(dto.Port) ? "*" : dto.Port,
            TargetNodeId = dto.Node ?? string.Empty
        };

        foreach ((string wireName, AliasDto? alias) in dto.Channels ?? [])
        {
            if (string.IsNullOrWhiteSpace(alias?.Channel))
            {
                warnings.Add($"{where}: '{wireName}' names no channel, so it was left unmapped.");
                continue;
            }

            rule.NameMap[wireName] = new ChannelAlias(
                alias.Channel.Trim(), (alias.Unit ?? string.Empty).Trim(),
                alias.Gain ?? 1.0, alias.Offset ?? 0.0);
        }

        return rule;
    }
}
