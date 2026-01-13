using System.Text.Json;

namespace WodMudClient;

public sealed class ConnectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public ConnectionsFile Load(string path)
    {
        if (!File.Exists(path))
        {
            var file = new ConnectionsFile();
            Save(path, file);
            return file;
        }

        var contents = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ConnectionsFile>(contents, JsonOptions) ?? new ConnectionsFile();
    }

    public void Save(string path, ConnectionsFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var json = JsonSerializer.Serialize(file, JsonOptions);
        File.WriteAllText(path, json);
    }
}

public sealed class ConnectionsFile
{
    public string? PreferredConnectionId { get; set; }
    public List<ConnectionProfile> Connections { get; set; } = new();
}

public sealed class ConnectionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3100;
    public bool UseTls { get; set; }
    public string? AccountName { get; set; }
    public string? Password { get; set; }
    public string? CharacterName { get; set; }
    public string AuthMethod { get; set; } = "guest";
    public AppearanceSettings Settings { get; set; } = new();
    public Dictionary<string, AppearanceSettings> CharacterSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string BuildUrl()
    {
        var scheme = UseTls ? "https" : "http";
        return $"{scheme}://{Host}:{Port}";
    }
}

public sealed class AppearanceSettings
{
    public string? Theme { get; set; }
    public ThemeConfig? CustomTheme { get; set; }
    public string? NavRingStyle { get; set; }
    public NavRingThemeConfig? NavRingTheme { get; set; }
    public KeybindSettings? KeyBindings { get; set; }
}

public sealed class NavRingThemeConfig
{
    public string? Foreground { get; set; }
    public string? Background { get; set; }
}
