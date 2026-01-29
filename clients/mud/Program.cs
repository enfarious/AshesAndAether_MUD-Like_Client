using AshesAndAether_Client;

var configPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "config.json");
var configDirectory = Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory;
var connectionsPath = args.Length > 1
    ? args[1]
    : Path.Combine(configDirectory, "connections.json");

var config = MudClientConfig.Load(configPath);
using var app = new MudClientApp(config, configPath, connectionsPath);
app.Run();
