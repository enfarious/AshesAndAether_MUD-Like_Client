using System.Text.Json;
using NStack;
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
    private readonly ThemeManager _themeManager = new();
    private SocketMessageMode _sendMode;
    private SocketMessageMode _receiveMode;
    private readonly List<CharacterSummary> _characters = new();
    private bool _canCreateCharacter;
    private bool _pendingCharacterDialog;

    private readonly List<string> _logLines = new();
    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private bool _showTimestamps = true;

    private TextView _logView = null!;
    private TextField _inputField = null!;
    private Label _statusLabel = null!;
    private Label _targetLabel = null!;
    private Label _movementLabel = null!;
    private Label _ringInfoLabel = null!;
    private PositionRingView _ringView = null!;
    private FrameView _macrosFrame = null!;
    private MenuBar _menuBar = null!;
    private StatusBar _statusBar = null!;
    private Window _window = null!;
    private string _activeThemeName = "ember";

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

        _menuBar = new MenuBar(new[]
        {
            new MenuBarItem("_Connection", new[]
            {
                new MenuItem("_Connect", "", () => _ = ConnectAsync(_config.ServerUrl)),
                new MenuItem("_Disconnect", "", () => _ = DisconnectAsync()),
                new MenuItem("_Login", "", ShowLoginDialog)
            }),
            new MenuBarItem("_View", new[]
            {
                new MenuItem("_Toggle Timestamps", "", ToggleTimestamps),
                new MenuItem("_Reload Config", "", ReloadConfig),
            }),
            new MenuBarItem("_Themes", BuildThemeMenuItems())
        });

        _statusBar = new StatusBar(new[]
        {
            new StatusItem(Key.F1, "~F1~ Help", ShowHelp),
            new StatusItem(Key.F2, "~F2~ Timestamps", ToggleTimestamps),
            new StatusItem(Key.F3, "~F3~ Login", ShowLoginDialog),
            new StatusItem(Key.F5, "~F5~ Connect", () => _ = ConnectAsync(_config.ServerUrl)),
            new StatusItem(Key.F6, "~F6~ Disconnect", () => _ = DisconnectAsync()),
            new StatusItem(Key.F10, "~F10~ Quit", Quit)
        });

        _window = new Window("World of Darkness - MUD Client v0.1")
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(1)
        };

        BuildMainLayout(_window);
        ApplyTheme(_config.Theme, logChange: false);

        top.Add(_menuBar, _window, _statusBar);
    }

    private void BuildMainLayout(Window window)
    {
        var sidebarWidth = 32;

        var header = new FrameView("Session")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 4
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

        _movementLabel = new Label("Movement: idle")
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(),
            Height = 1
        };

        header.Add(_statusLabel, _targetLabel, _movementLabel);

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
        sendButton.Clicked += () => SubmitInput(GetFieldText(_inputField));

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
                if (message.Type == "auth_success")
                {
                    UpdateAuthState(message.Payload);
                }

                foreach (var line in _router.Handle(message))
                {
                    AppendLogLine(line);
                }
                RefreshTargetState();
                RefreshMovementState();

                if (_pendingCharacterDialog)
                {
                    _pendingCharacterDialog = false;
                    ShowCharacterDialog();
                }
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
            SubmitInput(GetFieldText(_inputField));
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
        var movementResult = TryParseMovementCommand(text, out var movement, out var error);
        if (movementResult == MovementParseResult.Invalid)
        {
            AppendLogLine(error ?? "Invalid movement command.");
            return;
        }

        if (movementResult == MovementParseResult.Parsed)
        {
            await SendMovementCommandAsync(movement);
            AppendLogLine($"> {text}");
            return;
        }

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

    private void RefreshMovementState()
    {
        var speed = string.IsNullOrWhiteSpace(_state.CurrentSpeed) ? "idle" : _state.CurrentSpeed!.ToLowerInvariant();
        var heading = _state.CurrentHeading.HasValue ? HeadingToCompass(_state.CurrentHeading.Value) : "unknown";
        var directions = _state.AvailableDirections.Count > 0
            ? $"[{string.Join("] [", _state.AvailableDirections)}]"
            : "none";

        _movementLabel.Text = $"Movement: {speed} | Facing: {heading} | Available: {directions}";
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
        AppendLogLine("Client commands: /help /connect /disconnect /handshake /auth /select /create /target /clear-target /ping /raw /reload /theme /quit");
        AppendLogLine("Use macros for quick actions. Use the ring to send position intents relative to the current target.");
    }

    private void ShowLoginDialog()
    {
        var okButton = new Button("Login");
        var cancelButton = new Button("Cancel");
        var dialog = new Dialog("Authenticate", 60, 20, okButton, cancelButton);

        var methodLabel = new Label("Method:")
        {
            X = 1,
            Y = 1
        };
        var methods = new[] { (ustring)"Guest", "Token", "Creds" };
        var methodGroup = new RadioGroup(1, 2, methods, 0);

        var guestLabel = new Label("Guest name:")
        {
            X = 1,
            Y = 6
        };
        var guestField = new TextField("Wanderer")
        {
            X = 15,
            Y = 6,
            Width = 30
        };

        var tokenLabel = new Label("Token:")
        {
            X = 1,
            Y = 8
        };
        var tokenField = new TextField(string.Empty)
        {
            X = 15,
            Y = 8,
            Width = 40
        };

        var userLabel = new Label("Username:")
        {
            X = 1,
            Y = 10
        };
        var userField = new TextField(string.Empty)
        {
            X = 15,
            Y = 10,
            Width = 40
        };

        var passLabel = new Label("Password:")
        {
            X = 1,
            Y = 12
        };
        var passField = new TextField(string.Empty)
        {
            X = 15,
            Y = 12,
            Width = 40,
            Secret = true
        };

        void UpdateAuthFields()
        {
            var selected = methodGroup.SelectedItem;
            var showGuest = selected == 0;
            var showToken = selected == 1;
            var showCreds = selected == 2;

            guestLabel.Visible = showGuest;
            guestField.Visible = showGuest;
            tokenLabel.Visible = showToken;
            tokenField.Visible = showToken;
            userLabel.Visible = showCreds;
            userField.Visible = showCreds;
            passLabel.Visible = showCreds;
            passField.Visible = showCreds;
        }

        methodGroup.SelectedItemChanged += _ => UpdateAuthFields();
        UpdateAuthFields();

        dialog.Add(methodLabel, methodGroup, guestLabel, guestField, tokenLabel, tokenField, userLabel, userField, passLabel, passField);

        okButton.Clicked += () =>
        {
            switch (methodGroup.SelectedItem)
            {
                case 0:
                    _ = SendMessageAsync("auth", new { method = "guest", guestName = GetFieldText(guestField) });
                    AppendLogLine("Auth: guest request sent.");
                    break;
                case 1:
                    if (string.IsNullOrWhiteSpace(GetFieldText(tokenField)))
                    {
                        MessageBox.ErrorQuery("Auth", "Token required.", "Ok");
                        return;
                    }
                    _ = SendMessageAsync("auth", new { method = "token", token = GetFieldText(tokenField) });
                    AppendLogLine("Auth: token request sent.");
                    break;
                case 2:
                    if (string.IsNullOrWhiteSpace(GetFieldText(userField)) ||
                        string.IsNullOrWhiteSpace(GetFieldText(passField)))
                    {
                        MessageBox.ErrorQuery("Auth", "Username and password required.", "Ok");
                        return;
                    }
                    _ = SendMessageAsync("auth", new { method = "credentials", username = GetFieldText(userField), password = GetFieldText(passField) });
                    AppendLogLine("Auth: credentials request sent.");
                    break;
            }

            Application.RequestStop();
        };

        cancelButton.Clicked += () => Application.RequestStop();

        Application.Run(dialog);
    }

    private void ShowCharacterDialog()
    {
        if (_characters.Count == 0 && !_canCreateCharacter)
        {
            AppendLogLine("No characters available.");
            return;
        }

        var selectButton = new Button("Select");
        var createButton = new Button("Create");
        var cancelButton = new Button("Close");
        createButton.Enabled = _canCreateCharacter;

        var dialog = new Dialog("Character Select", 70, 20, selectButton, createButton, cancelButton);

        var listLabel = new Label("Characters:")
        {
            X = 1,
            Y = 1
        };
        var listItems = _characters
            .Select(c => $"{c.Name} (lvl {c.Level}) - {c.Location}")
            .ToList();
        var listView = new ListView(listItems)
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(2),
            Height = 8
        };

        var nameLabel = new Label("New name:")
        {
            X = 1,
            Y = 11
        };
        var nameField = new TextField(string.Empty)
        {
            X = 12,
            Y = 11,
            Width = 30
        };

        dialog.Add(listLabel, listView, nameLabel, nameField);

        selectButton.Clicked += () =>
        {
            if (_characters.Count == 0 || listView.SelectedItem < 0)
            {
                MessageBox.ErrorQuery("Select", "Choose a character.", "Ok");
                return;
            }

            var selected = _characters[Math.Min(listView.SelectedItem, _characters.Count - 1)];
            _ = SendMessageAsync("character_select", new { characterId = selected.Id });
            AppendLogLine($"Character select: {selected.Name}");
            Application.RequestStop();
        };

        createButton.Clicked += () =>
        {
            var name = GetFieldText(nameField).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.ErrorQuery("Create", "Name required.", "Ok");
                return;
            }

            _ = SendMessageAsync("character_create", new { name, appearance = new { description = "TBD" } });
            AppendLogLine($"Character create: {name}");
            Application.RequestStop();
        };

        cancelButton.Clicked += () => Application.RequestStop();

        Application.Run(dialog);
    }

    private void ToggleTimestamps()
    {
        _showTimestamps = !_showTimestamps;
        AppendLogLine($"Timestamps {(_showTimestamps ? "enabled" : "disabled")}.");
    }

    private static string GetFieldText(TextField field)
    {
        return field.Text?.ToString() ?? string.Empty;
    }

    private async Task SendMovementCommandAsync(MovementCommand movement)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (movement.Compass != null)
        {
            await SendMessageAsync("move", new { method = "compass", speed = movement.Speed, compass = movement.Compass, timestamp });
            return;
        }

        if (movement.Heading.HasValue)
        {
            await SendMessageAsync("move", new { method = "heading", speed = movement.Speed, heading = movement.Heading.Value, timestamp });
            return;
        }

        await SendMessageAsync("move", new { method = "heading", speed = movement.Speed, timestamp });
    }

    private MovementParseResult TryParseMovementCommand(string text, out MovementCommand movement, out string? error)
    {
        movement = default;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return MovementParseResult.NotMovement;
        }

        var trimmed = text.Trim();
        var separatorIndex = trimmed.IndexOf('.');
        if (separatorIndex < 0)
        {
            separatorIndex = trimmed.IndexOf(' ');
        }

        var speedToken = separatorIndex >= 0 ? trimmed[..separatorIndex] : trimmed;
        var directionToken = separatorIndex >= 0 ? trimmed[(separatorIndex + 1)..].Trim() : string.Empty;

        if (string.IsNullOrWhiteSpace(speedToken))
        {
            return MovementParseResult.NotMovement;
        }

        var speed = speedToken.ToLowerInvariant();
        if (speed is not ("walk" or "jog" or "run" or "stop"))
        {
            return MovementParseResult.NotMovement;
        }

        if (speed == "stop")
        {
            movement = new MovementCommand(speed, null, null);
            return MovementParseResult.Parsed;
        }

        if (string.IsNullOrWhiteSpace(directionToken))
        {
            movement = new MovementCommand(speed, null, null);
            return MovementParseResult.Parsed;
        }

        if (TryParseHeading(directionToken, out var heading))
        {
            movement = new MovementCommand(speed, heading, null);
            return MovementParseResult.Parsed;
        }

        if (TryParseCompass(directionToken, out var compass))
        {
            movement = new MovementCommand(speed, null, compass);
            return MovementParseResult.Parsed;
        }

        error = $"Invalid movement direction: {directionToken}";
        return MovementParseResult.Invalid;
    }

    private static bool TryParseHeading(string token, out int heading)
    {
        heading = 0;
        if (!int.TryParse(token, out var value))
        {
            return false;
        }

        if (value == 360)
        {
            heading = 0;
            return true;
        }

        heading = ((value % 360) + 360) % 360;
        return true;
    }

    private static bool TryParseCompass(string token, out string compass)
    {
        compass = string.Empty;
        var normalized = token.Replace("-", string.Empty).Replace("_", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        normalized = normalized.ToUpperInvariant();
        compass = normalized switch
        {
            "N" or "NORTH" => "N",
            "NE" or "NORTHEAST" => "NE",
            "E" or "EAST" => "E",
            "SE" or "SOUTHEAST" => "SE",
            "S" or "SOUTH" => "S",
            "SW" or "SOUTHWEST" => "SW",
            "W" or "WEST" => "W",
            "NW" or "NORTHWEST" => "NW",
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(compass);
    }

    private static string HeadingToCompass(double heading)
    {
        var dirs = new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        var index = (int)Math.Round(heading / 45.0) % dirs.Length;
        return dirs[Math.Clamp(index, 0, dirs.Length - 1)];
    }

    private enum MovementParseResult
    {
        NotMovement,
        Parsed,
        Invalid
    }

    private readonly record struct MovementCommand(string Speed, int? Heading, string? Compass);

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
        _config.Theme = updated.Theme;
        _config.CustomTheme = updated.CustomTheme;
        _config.PositionCommandTemplate = updated.PositionCommandTemplate;
        _config.RangeBands = updated.RangeBands;
        _config.Macros = updated.Macros;

        _sendMode = SocketMessageModeParser.Parse(_config.SendMode, SocketMessageMode.Event);
        _receiveMode = SocketMessageModeParser.Parse(_config.ReceiveMode, SocketMessageMode.Event);

        _ringView.RangeBands = _config.RangeBands;
        BuildMacroButtons();
        ApplyTheme(_config.Theme, logChange: true);
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
                if (parts.Length == 1)
                {
                    ShowLoginDialog();
                }
                else
                {
                    HandleAuthCommand(parts);
                }
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
            case "theme":
                HandleThemeCommand(parts);
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

    private void HandleThemeCommand(string[] parts)
    {
        if (parts.Length == 1 || string.Equals(parts[1], "list", StringComparison.OrdinalIgnoreCase))
        {
            var themes = GetAvailableThemes();
            AppendLogLine($"Themes: {string.Join(", ", themes)}");
            return;
        }

        ApplyTheme(parts[1], logChange: true);
    }

    private MenuItem[] BuildThemeMenuItems()
    {
        var items = new List<MenuItem>();
        foreach (var theme in GetAvailableThemes())
        {
            items.Add(new MenuItem(theme, "", () => ApplyTheme(theme, logChange: true)));
        }

        return items.ToArray();
    }

    private IReadOnlyList<string> GetAvailableThemes()
    {
        var themes = _themeManager.PresetNames.ToList();
        themes.Add("custom");
        return themes;
    }

    private void ApplyTheme(string themeName, bool logChange)
    {
        if (string.Equals(themeName, "custom", StringComparison.OrdinalIgnoreCase) &&
            !_themeManager.HasCustomTheme(_config.CustomTheme))
        {
            AppendLogLine("Custom theme is not configured.");
            return;
        }

        var scheme = _themeManager.Resolve(themeName, _config.CustomTheme, out var resolvedName);
        _activeThemeName = resolvedName;
        _config.Theme = resolvedName;

        Application.Top.ColorScheme = scheme;
        _menuBar.ColorScheme = scheme;
        _statusBar.ColorScheme = scheme;
        _window.ColorScheme = scheme;
        _logView.ColorScheme = scheme;
        _inputField.ColorScheme = scheme;
        _macrosFrame.ColorScheme = scheme;
        _ringView.ColorScheme = scheme;
        _statusLabel.ColorScheme = scheme;
        _targetLabel.ColorScheme = scheme;
        _movementLabel.ColorScheme = scheme;
        _ringInfoLabel.ColorScheme = scheme;

        Application.Top.SetNeedsDisplay();
        _window.SetNeedsDisplay();

        if (logChange)
        {
            AppendLogLine($"Theme set to {_activeThemeName}.");
        }
    }

    private void UpdateAuthState(JsonElement payload)
    {
        _characters.Clear();
        _canCreateCharacter = payload.GetPropertyOrDefault("canCreateCharacter")?.GetBoolean() ?? false;

        if (payload.TryGetProperty("characters", out var characters) &&
            characters.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in characters.EnumerateArray())
            {
                var id = entry.GetPropertyOrDefault("id")?.GetString() ?? string.Empty;
                var name = entry.GetPropertyOrDefault("name")?.GetString() ?? "Unknown";
                var level = entry.GetPropertyOrDefault("level")?.GetInt32() ?? 0;
                var location = entry.GetPropertyOrDefault("location")?.GetString() ?? "Unknown";
                _characters.Add(new CharacterSummary(id, name, level, location));
            }
        }

        _pendingCharacterDialog = true;
    }

    public void Dispose()
    {
        _transport.Dispose();
    }
}

internal sealed record CharacterSummary(string Id, string Name, int Level, string Location);
