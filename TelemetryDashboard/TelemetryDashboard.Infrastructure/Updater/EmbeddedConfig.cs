namespace TelemetryDashboard.Infrastructure.Updater;

/// <summary>Settings recovered from an embedded configuration blob.</summary>
public sealed class EmbeddedConfig
{
    /// <summary>True when the blob was unreadable and built-in defaults are in force.</summary>
    public bool UseDefaults { get; init; }

    public int StreamingPort { get; init; } = 8080;
    public string Theme { get; init; } = "Dark";
    public string Language { get; init; } = "ko-KR";

    /// <summary>Why defaults were substituted, when they were.</summary>
    public string? FallbackReason { get; init; }
}
