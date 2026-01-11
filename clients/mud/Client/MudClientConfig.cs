using System.Text.Json;

namespace WodMudClient;

public sealed class MudClientConfig
{
    public string ServerUrl { get; set; } = "http://localhost:3000";
    public string SendMode { get; set; } = "event";
    public string ReceiveMode { get; set; } = "event";
    public string SendEventName { get; set; } = "message";
    public string ReceiveEventName { get; set; } = "message";
    public string ProtocolVersion { get; set; } = "1.0.0";
    public string ClientVersion { get; set; } = "0.1.0";
    public int MaxUpdateRate { get; set; } = 1;
    public bool AutoConnect { get; set; } = true;
    public string DefaultCommandType { get; set; } = "command";
    public string Theme { get; set; } = "ember";
    public ThemeConfig CustomTheme { get; set; } = new();
    public string PositionCommandTemplate { get; set; } = "position {target} {range_band} {angle_deg}";
    public List<string> RangeBands { get; set; } = new()
    {
        "melee_close",
        "melee_long",
        "ranged_short",
        "ranged_long"
    };
    public List<MacroDefinition> Macros { get; set; } = new();
    public bool ShowDiagnosticInfo { get; set; } = false;

    public static MudClientConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            var config = new MudClientConfig();
            var json = JsonSerializer.Serialize(config, JsonOptions);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, json);
            return config;
        }

        var contents = File.ReadAllText(path);
        return JsonSerializer.Deserialize<MudClientConfig>(contents, JsonOptions) ?? new MudClientConfig();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}

public sealed class MacroDefinition
{
    public string Label { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
}

public sealed class ThemeConfig
{
    public string? NormalForeground { get; set; }
    public string? NormalBackground { get; set; }
    public string? AccentForeground { get; set; }
    public string? AccentBackground { get; set; }
    public string? MutedForeground { get; set; }
    public string? MutedBackground { get; set; }
}
