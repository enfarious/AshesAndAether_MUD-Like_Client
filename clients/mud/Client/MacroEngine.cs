namespace AshesAndAether_Client;

public sealed class MacroEngine
{
    public string Resolve(string template, MacroContext context)
    {
        return template
            .Replace("{target}", context.TargetToken ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{self}", context.SelfToken ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{angle_deg}", context.AngleDegrees?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{range_band}", context.RangeBand ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{range_units}", context.RangeUnits?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class MacroContext
{
    public string? TargetToken { get; set; }
    public string? SelfToken { get; set; }
    public int? AngleDegrees { get; set; }
    public string? RangeBand { get; set; }
    public int? RangeUnits { get; set; }
}
