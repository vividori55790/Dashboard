namespace TelemetryDashboard.Core.Models;

public enum RuleType
{
    Prefix,
    Json,
    Columns
}

public sealed class RoutingRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public RuleType RuleType { get; set; } = RuleType.Prefix;

    public PacketFormat Format
    {
        get => RuleType switch
        {
            RuleType.Prefix => PacketFormat.Prefix,
            RuleType.Json => PacketFormat.Json,
            RuleType.Columns => PacketFormat.Columns,
            _ => PacketFormat.Unknown
        };
        set => RuleType = value switch
        {
            PacketFormat.Prefix => RuleType.Prefix,
            PacketFormat.Json => RuleType.Json,
            PacketFormat.Columns => RuleType.Columns,
            _ => RuleType.Prefix
        };
    }
    public string Tag { get; set; } = string.Empty; // e.g. "$DAB" or "device:PSFB"
    public string Port { get; set; } = "*"; // COM port filter ("*" for any)
    public string TargetNodeId { get; set; } = string.Empty;

    // Index mapping for CSV/Prefix rules: Column Index -> Variable Name
    public Dictionary<int, string> IndexMap { get; set; } = new();

    // Property mapping for JSON rules: JSON Property Key -> Variable Name
    public Dictionary<string, string> JsonMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Dynamic gain/offset adjustments per variable: Variable -> (Gain, Offset)
    public Dictionary<string, (double Gain, double Offset)> Calibrations { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Linked formulas evaluated when this rule matches
    public List<string> Formulas { get; set; } = new();
}
