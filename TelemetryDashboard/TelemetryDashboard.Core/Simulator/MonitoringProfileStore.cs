using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace TelemetryDashboard.Core.Simulator;

/// <summary>How the profile list came to be what it is.</summary>
public enum ProfileSourceStatus
{
    /// <summary>No profile file beside the executable. The built-in profiles are all there is.</summary>
    NoFile,

    /// <summary>The profile file was read and its profiles are in the list.</summary>
    Loaded,

    /// <summary>The file exists but could not be used. Only the built-in profiles are loaded.</summary>
    Invalid
}

/// <summary>The profiles available this session, and an account of where they came from.</summary>
public sealed class MonitoringProfileSet
{
    public required IReadOnlyList<MonitoringProfile> Profiles { get; init; }

    /// <summary>The profile to select at startup. Always the built-in generic one.</summary>
    public static MonitoringProfile Default => MonitoringProfileLibrary.Generic;

    public required ProfileSourceStatus Status { get; init; }

    /// <summary>Plain-language account of what happened, ready to put in front of an operator.</summary>
    public required string Message { get; init; }

    /// <summary>The file that was looked for, named so the message can be acted on.</summary>
    public required string Path { get; init; }
}

/// <summary>
/// Reads extra monitoring profiles from a JSON file beside the executable.
/// </summary>
/// <remarks>
/// The failure behaviour is the point. A profile decides which channels an operator is looking at
/// and what their limits are, so quietly falling back to a different one when the file is broken
/// would put somebody else's bus voltages on screen under the name that was asked for. A file that
/// cannot be used is reported by name with the reason, and the session continues on the built-in
/// profiles only — never on a substitute that resembles what was requested.
/// </remarks>
public static class MonitoringProfileStore
{
    public const string FileName = "profiles.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Loads the built-in profiles plus any the given directory's profile file defines.</summary>
    public static MonitoringProfileSet Load(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        string path = System.IO.Path.Combine(directory, FileName);

        if (!File.Exists(path))
        {
            return new MonitoringProfileSet
            {
                Profiles = MonitoringProfileLibrary.BuiltIn,
                Status = ProfileSourceStatus.NoFile,
                Path = path,
                Message = $"프로파일 파일이 없어 내장 프로파일만 사용합니다: {path}"
            };
        }

        ProfileFileDto? file;
        try
        {
            file = JsonSerializer.Deserialize<ProfileFileDto>(
                File.ReadAllText(path), ReadOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException
                                      or UnauthorizedAccessException or NotSupportedException)
        {
            return Rejected(path, ex.Message);
        }

        if (file?.Profiles is not { Count: > 0 })
        {
            return Rejected(path, "profiles 배열이 비어 있거나 없습니다.");
        }

        var profiles = new List<MonitoringProfile>(MonitoringProfileLibrary.BuiltIn);
        var problems = new List<string>();

        foreach (ProfileDto dto in file.Profiles)
        {
            MonitoringProfile? profile = MonitoringProfileReader.Convert(dto, profiles, problems);
            if (profile is not null) profiles.Add(profile);
        }

        int added = profiles.Count - MonitoringProfileLibrary.BuiltIn.Count;
        if (added == 0)
        {
            return Rejected(path, string.Join(" ", problems));
        }

        string note = problems.Count > 0
            ? " 사용하지 못한 항목: " + string.Join(" ", problems)
            : string.Empty;

        return new MonitoringProfileSet
        {
            Profiles = profiles,
            Status = ProfileSourceStatus.Loaded,
            Path = path,
            Message = $"{path} 에서 프로파일 {added.ToString(CultureInfo.InvariantCulture)}개를 읽었습니다.{note}"
        };
    }

    private static MonitoringProfileSet Rejected(string path, string reason) => new()
    {
        Profiles = MonitoringProfileLibrary.BuiltIn,
        Status = ProfileSourceStatus.Invalid,
        Path = path,
        Message = $"프로파일 파일을 읽지 못해 내장 프로파일만 사용합니다. 파일: {path} / 원인: {reason}"
    };
}
