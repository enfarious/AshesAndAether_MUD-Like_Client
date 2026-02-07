using System.Text.Json;

namespace AshesAndAether_Client;

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
    public bool AutoLogin { get; set; } = true;
    public bool AutoConnect { get; set; } = false;
    public string DefaultCommandType { get; set; } = "command";
    public string Theme { get; set; } = "ember";
    public string NavRingStyle { get; set; } = "compass";
    public NavRingThemeConfig NavRingTheme { get; set; } = new();
    public ThemeConfig CustomTheme { get; set; } = new();
    public ChatStyleConfig ChatStyle { get; set; } = new();
    public CombatDisplayConfig CombatDisplay { get; set; } = new();
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
    public bool ShowDevNotices { get; set; } = false;
    public bool WrapLogLines { get; set; } = true;
    public KeybindSettings KeyBindings { get; set; } = KeybindSettings.CreateDefaults();

    public static MudClientConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            var defaultConfig = new MudClientConfig();
            var json = JsonSerializer.Serialize(defaultConfig, JsonOptions);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, json);
            return defaultConfig;
        }

        var contents = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<MudClientConfig>(contents, JsonOptions) ?? new MudClientConfig();
        config.ChatStyle ??= new ChatStyleConfig();
        config.CombatDisplay ??= new CombatDisplayConfig();
        if (config.CombatDisplay.ColorKeys == null || config.CombatDisplay.ColorKeys.Count == 0)
        {
            config.CombatDisplay.ColorKeys = new CombatDisplayConfig().ColorKeys;
        }
        else
        {
            config.CombatDisplay.ColorKeys = new Dictionary<string, string>(
                config.CombatDisplay.ColorKeys,
                StringComparer.OrdinalIgnoreCase);
        }
        config.KeyBindings ??= KeybindSettings.CreateDefaults();
        if (config.KeyBindings.Commands.Count == 0)
        {
            config.KeyBindings.Commands = KeybindSettings.CreateDefaults().Commands;
        }
        return config;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
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

public sealed class ChatStyleConfig
{
    public string? Foreground { get; set; }
    public string? Background { get; set; }
}

public sealed class CombatDisplayConfig
{
    public string Style { get; set; } = "compact";
    public bool ShowFx { get; set; } = false;
    public bool ShowImpactFx { get; set; } = true;
    public bool UseColorKeys { get; set; } = true;
    public Dictionary<string, string> ColorKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["player.color.hit"] = "white",
        ["player.color.crit"] = "brightyellow",
        ["player.color.crit_pen"] = "brightyellow",
        ["player.color.penetrate"] = "brightyellow",
        ["player.color.deflect"] = "brightcyan",
        ["player.color.glance"] = "gray",
        ["player.color.miss"] = "darkgray",
        ["player.color.take_damage"] = "brightred",
        ["player.color.kill"] = "brightmagenta",
        ["player.color.die"] = "brightred"
    };
}

public sealed class KeybindSettings
{
    public Dictionary<string, string> Bindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Commands { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static KeybindSettings CreateDefaults()
    {
        return new KeybindSettings
        {
            Bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ability.1"] = "1",
                ["ability.2"] = "2",
                ["ability.3"] = "3",
                ["ability.4"] = "4",
                ["ability.5"] = "5",
                ["ability.6"] = "6",
                ["ability.7"] = "7",
                ["ability.8"] = "8",
                ["quick.1"] = "9",
                ["quick.2"] = "0",
                ["quick.3"] = "-",
                ["quick.4"] = "=",
                ["companion.prev"] = ",",
                ["companion.next"] = "."
            },
            Commands = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ability.1"] = "/cast ability1",
                ["ability.2"] = "/cast ability2",
                ["ability.3"] = "/cast ability3",
                ["ability.4"] = "/cast ability4",
                ["ability.5"] = "/cast ability5",
                ["ability.6"] = "/cast ability6",
                ["ability.7"] = "/cast ability7",
                ["ability.8"] = "/cast ability8",
                ["quick.1"] = "/use item1",
                ["quick.2"] = "/use item2",
                ["quick.3"] = "/use item3",
                ["quick.4"] = "/use item4",
                ["companion.prev"] = "/companion prev",
                ["companion.next"] = "/companion next"
            }
        };
    }
}
