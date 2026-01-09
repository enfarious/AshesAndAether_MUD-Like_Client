using System.Text.Json;
using Terminal.Gui;

namespace WodMudClient;

public sealed class MudClientApp : IDisposable
{
    private const int MaxLogLines = 2000;

    private readonly MudClientConfig _config;
    private readonly string _configPath;
    private readonly SocketIoTransport _transport = new();
    private readonly StateStore _state = new();
    private readonly MacroEngine _macroEngine = new();
    private readonly MessageRouter _router;
    private SocketMessageMode _sendMode;
    private SocketMessageMode _receiveMode;

    private readonly List<string> _logLines = new();
    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private bool _showTimestamps = true;

    private TextView _logView = null!;
    private TextField _inputField = null!;
    private Label _statusLabel = null!;
    private Label _targetLabel = null!;
    private Label _ringInfoLabel = null!;
    private PositionRingView _ringView = null!;
    private FrameView _macrosFrame = null!;

    public MudClientApp(MudClientConfig config, string configPath)
    {
        _config = config;
        _configPath = configPath;
        _router = new MessageRouter(_state);
        _sendMode = SocketMessageModeParser.Parse(_config.SendMode, SocketMessageMode.Event);
        _receiveMode = SocketMessageModeParser.Parse(_config.ReceiveMode, SocketMessageMode.Event);
    }

    public void Run()
    {
        Application.Init();
        BuildUi();
        WireTransport();

        if (_config.AutoConnect)
        {
            _ = ConnectAsync(_config.ServerUrl);
        }

        Application.Run();
        Application.Shutdown();
    }

    private void BuildUi()
    {
        var top = Application.Top;

        var menu = new MenuBar(new[]
        {
            new MenuBarItem("_Connection", new[]
            {
                new MenuItem("_Connect", "", () => _ = ConnectAsync(_config.ServerUrl)),
                new MenuItem("_Disconnect", "", () => _ = DisconnectAsync())
            }),
            new MenuBarItem("_View", new[]
            {
                new MenuItem("_Toggle Timestamps", "", ToggleTimestamps),
                new MenuItem("_Reload Config", "", ReloadConfig)
            })
        });

        var statusBar = new StatusBar(new[]
        {
            new StatusItem(Key.F1, "~F1~ Help", ShowHelp),
            new StatusItem(Key.F2, "~F2~ Timestamps", ToggleTimestamps),
            new StatusItem(Key.F5, "~F5~ Connect", () => _ = ConnectAsync(_config.ServerUrl)),
            new StatusItem(Key.F6, "~F6~ Disconnect", () => _ = DisconnectAsync()),
            new StatusItem(Key.F10, "~F10~ Quit", Quit)
        });

        var window = new Window("World of Darkness - MUD Client v0.1")
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(1)
        };

        BuildMainLayout(window);

        top.Add(menu, window, statusBar);
    }

    private void BuildMainLayout(Window window)
    {
        var sidebarWidth = 32;

        var header = new FrameView("Session")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 3
        };

        _statusLabel = new Label("Status: disconnected")
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1
        };

        _targetLabel = new Label("Target: none")
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(),
            Height = 1
        };

        header.Add(_statusLabel, _targetLabel);

        var logFrame = new FrameView("Log")
        {
            X = 0,
            Y = Pos.Bottom(header),
            Width = Dim.Fill(sidebarWidth),
            Height = Dim.Fill(4)
        };

        _logView = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = true
        };
        logFrame.Add(_logView);

        var inputFrame = new FrameView("Input")
        {
            X = 0,
            Y = Pos.Bottom(logFrame),
            Width = Dim.Fill(),
            Height = 4
        };

        _inputField = new TextField(string.Empty)
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(12),
            Height = 1
        };
        _inputField.KeyPress += InputFieldOnKeyPress;

        var sendButton = new Button("Send")
        {
            X = Pos.Right(_inputField) + 1,
            Y = 0
        };
        sendButton.Clicked += () => SubmitInput(_inputField.Text.ToString());

        inputFrame.Add(_inputField, sendButton);

        var sidebar = new View
        {
            X = Pos.Right(logFrame),
            Y = Pos.Bottom(header),
            Width = sidebarWidth,
            Height = Dim.Fill(4)
        };

        _macrosFrame = new FrameView("Macros")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(12)
        };
        BuildMacroButtons();

        var ringFrame = new FrameView("Position")
        {
            X = 0,
            Y = Pos.Bottom(_macrosFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        var initialBand = _config.RangeBands.FirstOrDefault() ?? "unknown";
        _ringInfoLabel = new Label($"Angle: 0 deg  Band: {initialBand}")
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1
        };

        _ringView = new PositionRingView
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = Dim.Fill(1),
            CanFocus = true,
            RangeBands = _config.RangeBands,
            WantMousePositionReports = true
        };
        _ringView.SelectionConfirmed += OnRingSelectionConfirmed;
        _ringView.SelectionChanged += OnRingSelectionChanged;

        ringFrame.Add(_ringInfoLabel, _ringView);

        sidebar.Add(_macrosFrame, ringFrame);

        window.Add(header, logFrame, inputFrame, sidebar);
    }

    private void BuildMacroButtons()
    {
        _macrosFrame.RemoveAll();

        var macros = _config.Macros;
        var columns = 2;
        var rows = (int)Math.Ceiling(macros.Count / (double)columns);
        var height = Math.Max(3, rows + 2);
        _macrosFrame.Height = height;

        for (var i = 0; i < macros.Count; i++)
        {
            var macro = macros[i];
            var row = i / columns;
            var col = i % columns;

            var button = new Button(macro.Label)
            {
                X = col == 0 ? 0 : Pos.Percent(50),
                Y = row,
                Width = Dim.Percent(50),
                Height = 1
            };

            button.Clicked += () => SendMacro(macro.Command);
            _macrosFrame.Add(button);
        }
    }

    private void WireTransport()
    {
        _transport.Connected += () =>
        {
            Application.MainLoop.Invoke(() =>
            {
                _statusLabel.Text = $"Status: connected ({_config.ServerUrl})";
                AppendLogLine("Connected.");
            });
            _ = SendHandshakeAsync();
        };

        _transport.Disconnected += reason =>
        {
            Application.MainLoop.Invoke(() =>
            {
                _statusLabel.Text = "Status: disconnected";
                AppendLogLine($"Disconnected: {reason}");
            });
        };

        _transport.MessageReceived += json =>
        {
            if (!IncomingMessage.TryParse(json, out var message) || message == null)
            {
                Application.MainLoop.Invoke(() => AppendLogLine($"< {json}"));
                return;
            }

            Application.MainLoop.Invoke(() =>
            {
                foreach (var line in _router.Handle(message))
                {
                    AppendLogLine(line);
                }
                RefreshTargetState();
            });
        };
    }

    private async Task ConnectAsync(string serverUrl)
    {
        try
        {
            AppendLogLine($"Connecting to {serverUrl}...");
            await _transport.ConnectAsync(serverUrl, _config.ReceiveEventName, _receiveMode);
        }
        catch (Exception ex)
        {
            AppendLogLine($"Connect failed: {ex.Message}");
        }
    }

    private async Task DisconnectAsync()
    {
        if (!_transport.IsConnected)
        {
            AppendLogLine("Already disconnected.");
            return;
        }

        await SendMessageAsync("disconnect", new { reason = "user_logout" });
        await _transport.DisconnectAsync();
    }

    private async Task SendHandshakeAsync()
    {
        var payload = new
        {
            protocolVersion = _config.ProtocolVersion,
            clientType = "text",
            clientVersion = _config.ClientVersion,
            capabilities = new
            {
                graphics = false,
                audio = false,
                input = new[] { "keyboard", "mouse" },
                maxUpdateRate = _config.MaxUpdateRate
            }
        };

        await SendMessageAsync("handshake", payload);
    }

    private async Task SendMessageAsync(string type, object payload)
    {
        if (_sendMode == SocketMessageMode.Event)
        {
            await _transport.SendAsync(type, payload);
            return;
        }

        var message = new OutgoingMessage(type, payload, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await _transport.SendAsync(_config.SendEventName, message);
    }

    private async Task SendRawAsync(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (_sendMode == SocketMessageMode.Event)
            {
                string? eventName = null;
                JsonElement payload = doc.RootElement.Clone();

                if (doc.RootElement.TryGetProperty("event", out var eventProp))
                {
                    eventName = eventProp.GetString();
                    if (doc.RootElement.TryGetProperty("payload", out var payloadProp))
                    {
                        payload = payloadProp.Clone();
                    }
                }
                else if (doc.RootElement.TryGetProperty("type", out var typeProp))
                {
                    eventName = typeProp.GetString();
                    if (doc.RootElement.TryGetProperty("payload", out var payloadProp))
                    {
                        payload = payloadProp.Clone();
                    }
                }

                if (string.IsNullOrWhiteSpace(eventName))
                {
                    eventName = _config.SendEventName;
                }

                await _transport.SendAsync(eventName!, payload);
                return;
            }

            await _transport.SendAsync(_config.SendEventName, doc.RootElement.Clone());
        }
        catch (JsonException)
        {
            AppendLogLine("Raw JSON parse failed.");
        }
    }

    private void SubmitInput(string? input)
    {
        var text = input?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _history.Add(text);
        _historyIndex = _history.Count;
        _inputField.Text = string.Empty;

        if (text.StartsWith("/"))
        {
            HandleClientCommand(text[1..]);
            return;
        }

        _ = SendTextCommandAsync(text);
    }

    private void InputFieldOnKeyPress(View.KeyEventEventArgs args)
    {
        var key = args.KeyEvent.Key;
        if (key == Key.Enter)
        {
            SubmitInput(_inputField.Text.ToString());
            args.Handled = true;
            return;
        }

        if (key == Key.CursorUp)
        {
            CycleHistory(-1);
            args.Handled = true;
            return;
        }

        if (key == Key.CursorDown)
        {
            CycleHistory(1);
            args.Handled = true;
        }
    }

    private void CycleHistory(int direction)
    {
        if (_history.Count == 0)
        {
            return;
        }

        _historyIndex = Math.Clamp(_historyIndex + direction, 0, _history.Count - 1);
        _inputField.Text = _history[_historyIndex];
    }

    private async Task SendTextCommandAsync(string text)
    {
        var payload = new { text };
        await SendMessageAsync(_config.DefaultCommandType, payload);
        AppendLogLine($"> {text}");
    }

    private void SendMacro(string commandTemplate)
    {
        var targetToken = _state.CurrentTargetToken;
        if (commandTemplate.Contains("{target}", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(targetToken))
        {
            AppendLogLine("Macro requires a target.");
            return;
        }

        var context = new MacroContext
        {
            TargetToken = targetToken
        };

        var command = _macroEngine.Resolve(commandTemplate, context);
        _ = SendTextCommandAsync(command);
        _inputField.SetFocus();
    }

    private void OnRingSelectionChanged(PositionSelection selection)
    {
        _ringInfoLabel.Text = $"Angle: {selection.AngleDegrees} deg  Band: {selection.RangeBand}";
    }

    private void OnRingSelectionConfirmed(PositionSelection selection)
    {
        if (string.IsNullOrWhiteSpace(_state.CurrentTargetToken))
        {
            AppendLogLine("Select a target before using the ring.");
            return;
        }

        var context = new MacroContext
        {
            TargetToken = _state.CurrentTargetToken,
            AngleDegrees = selection.AngleDegrees,
            RangeBand = selection.RangeBand
        };

        var command = _macroEngine.Resolve(_config.PositionCommandTemplate, context);
        _ = SendTextCommandAsync(command);
        _inputField.SetFocus();
    }

    private void RefreshTargetState()
    {
        var targetToken = _state.CurrentTargetName ?? _state.CurrentTargetId;
        _targetLabel.Text = $"Target: {(string.IsNullOrWhiteSpace(targetToken) ? "none" : targetToken)}";
        _ringView.HasTarget = !string.IsNullOrWhiteSpace(targetToken);
        _ringView.SetNeedsDisplay();
    }

    private void AppendLogLine(string line)
    {
        var formatted = _showTimestamps
            ? $"[{DateTime.Now:HH:mm:ss}] {line}"
            : line;

        _logLines.Add(formatted);
        if (_logLines.Count > MaxLogLines)
        {
            _logLines.RemoveRange(0, _logLines.Count - MaxLogLines);
        }

        _logView.Text = string.Join('\n', _logLines);
        _logView.MoveEnd();
    }

    private void ShowHelp()
    {
        AppendLogLine("Client commands: /help /connect /disconnect /handshake /auth /select /create /target /clear-target /ping /raw /reload /quit");
        AppendLogLine("Use macros for quick actions. Use the ring to send position intents relative to the current target.");
    }

    private void ToggleTimestamps()
    {
        _showTimestamps = !_showTimestamps;
        AppendLogLine($"Timestamps {(_showTimestamps ? "enabled" : "disabled")}.");
    }

    private void ReloadConfig()
    {
        var updated = MudClientConfig.Load(_configPath);
        _config.ServerUrl = updated.ServerUrl;
        _config.SendMode = updated.SendMode;
        _config.ReceiveMode = updated.ReceiveMode;
        _config.SendEventName = updated.SendEventName;
        _config.ReceiveEventName = updated.ReceiveEventName;
        _config.ProtocolVersion = updated.ProtocolVersion;
        _config.ClientVersion = updated.ClientVersion;
        _config.MaxUpdateRate = updated.MaxUpdateRate;
        _config.DefaultCommandType = updated.DefaultCommandType;
        _config.PositionCommandTemplate = updated.PositionCommandTemplate;
        _config.RangeBands = updated.RangeBands;
        _config.Macros = updated.Macros;

        _sendMode = SocketMessageModeParser.Parse(_config.SendMode, SocketMessageMode.Event);
        _receiveMode = SocketMessageModeParser.Parse(_config.ReceiveMode, SocketMessageMode.Event);

        _ringView.RangeBands = _config.RangeBands;
        BuildMacroButtons();
        AppendLogLine("Config reloaded.");
    }

    private void Quit()
    {
        _ = DisconnectAsync();
        Application.RequestStop();
    }

    private void HandleClientCommand(string commandLine)
    {
        var parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return;
        }

        var command = parts[0].ToLowerInvariant();
        switch (command)
        {
            case "help":
                ShowHelp();
                break;
            case "connect":
                {
                    var url = parts.Length > 1 ? parts[1] : _config.ServerUrl;
                    _config.ServerUrl = url;
                    _ = ConnectAsync(url);
                    break;
                }
            case "disconnect":
                _ = DisconnectAsync();
                break;
            case "handshake":
                _ = SendHandshakeAsync();
                break;
            case "auth":
                HandleAuthCommand(parts);
                break;
            case "select":
                if (parts.Length < 2)
                {
                    AppendLogLine("Usage: /select <characterId>");
                    break;
                }
                _ = SendMessageAsync("character_select", new { characterId = parts[1] });
                break;
            case "create":
                if (parts.Length < 2)
                {
                    AppendLogLine("Usage: /create <name>");
                    break;
                }
                _ = SendMessageAsync("character_create", new { name = parts[1], appearance = new { description = "TBD" } });
                break;
            case "target":
                HandleTargetCommand(parts);
                break;
            case "clear-target":
                _state.ClearTarget();
                RefreshTargetState();
                break;
            case "ping":
                _ = SendMessageAsync("ping", new { timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
                break;
            case "raw":
                if (parts.Length < 2)
                {
                    AppendLogLine("Usage: /raw <json>");
                    break;
                }
                var raw = commandLine.Substring(commandLine.IndexOf(' ') + 1);
                _ = SendRawAsync(raw);
                break;
            case "reload":
                ReloadConfig();
                break;
            case "quit":
                Quit();
                break;
            default:
                AppendLogLine($"Unknown command: {command}");
                break;
        }
    }

    private void HandleAuthCommand(string[] parts)
    {
        if (parts.Length < 2)
        {
            AppendLogLine("Usage: /auth <guest|token|creds> [args]");
            return;
        }

        var method = parts[1].ToLowerInvariant();
        switch (method)
        {
            case "guest":
                {
                    var name = parts.Length > 2 ? parts[2] : "Wanderer";
                    _ = SendMessageAsync("auth", new { method = "guest", guestName = name });
                    break;
                }
            case "token":
                if (parts.Length < 3)
                {
                    AppendLogLine("Usage: /auth token <token>");
                    return;
                }
                _ = SendMessageAsync("auth", new { method = "token", token = parts[2] });
                break;
            case "creds":
                if (parts.Length < 4)
                {
                    AppendLogLine("Usage: /auth creds <username> <password>");
                    return;
                }
                _ = SendMessageAsync("auth", new { method = "credentials", username = parts[2], password = parts[3] });
                break;
            default:
                AppendLogLine("Auth methods: guest, token, creds");
                break;
        }
    }

    private void HandleTargetCommand(string[] parts)
    {
        if (parts.Length < 2)
        {
            if (_state.Entities.Count == 0)
            {
                AppendLogLine("No known entities to target.");
                return;
            }

            AppendLogLine("Known entities:");
            foreach (var entity in _state.Entities)
            {
                AppendLogLine($"- {entity.Name} ({entity.Id})");
            }
            return;
        }

        var token = parts[1];
        if (_state.TryResolveTarget(token, out var targetId, out var targetName))
        {
            _state.SetTarget(targetId, targetName);
        }
        else
        {
            _state.SetTargetToken(token);
        }

        RefreshTargetState();
    }

    public void Dispose()
    {
        _transport.Dispose();
    }
}
