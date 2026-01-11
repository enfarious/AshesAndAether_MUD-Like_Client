using WodMudClient;

var configPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "config.json");
var connectionsPath = args.Length > 1
    ? args[1]
    : Path.Combine(AppContext.BaseDirectory, "connections.json");

var config = MudClientConfig.Load(configPath);
using var app = new MudClientApp(config, configPath, connectionsPath);
app.Run();
