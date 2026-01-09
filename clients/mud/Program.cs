using WodMudClient;

var configPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "config.json");

var config = MudClientConfig.Load(configPath);
using var app = new MudClientApp(config, configPath);
app.Run();
