using System;
using System.Globalization;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Text.Json;
using NStack;
using Terminal.Gui;

namespace WodMudClient;

public sealed class MudClientApp : IDisposable
{
    private const int MaxLogLines = 2000;
    private static readonly string[] NavigationSpeeds = { "run", "jog", "walk" };
    private static readonly IReadOnlyList<KeybindAction> KeybindActions = new[]
    {
        new KeybindAction("ability.1", "Ability Slot 1", "1", "/cast ability1"),
        new KeybindAction("ability.2", "Ability Slot 2", "2", "/cast ability2"),
        new KeybindAction("ability.3", "Ability Slot 3", "3", "/cast ability3"),
        new KeybindAction("ability.4", "Ability Slot 4", "4", "/cast ability4"),
        new KeybindAction("ability.5", "Ability Slot 5", "5", "/cast ability5"),
        new KeybindAction("ability.6", "Ability Slot 6", "6", "/cast ability6"),
        new KeybindAction("ability.7", "Ability Slot 7", "7", "/cast ability7"),
        new KeybindAction("ability.8", "Ability Slot 8", "8", "/cast ability8"),
        new KeybindAction("quick.1", "Quick Item 1", "9", "/use item1"),
        new KeybindAction("quick.2", "Quick Item 2", "0", "/use item2"),
        new KeybindAction("quick.3", "Quick Item 3", "-", "/use item3"),
        new KeybindAction("quick.4", "Quick Item 4", "=", "/use item4"),
        new KeybindAction("companion.prev", "Companion Mode Prev", ",", "/companion prev"),
        new KeybindAction("companion.next", "Companion Mode Next", ".", "/companion next")
    };
    private static readonly Dictionary<string, KeybindAction> KeybindActionLookup = KeybindActions
        .ToDictionary(action => action.Id, action => action, StringComparer.OrdinalIgnoreCase);

    private readonly MudClientConfig _config;
    private readonly string _configPath;
    private readonly string _connectionsPath;
    private readonly ConnectionStore _connectionStore = new();
    private ConnectionsFile _connections = new();
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
    private readonly Dictionary<string, string> _resolvedKeyBindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _keybindActionsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _resolvedKeyCommands = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<LogEntry> _logEntries = new();
    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private bool _showTimestamps = true;

    private ListView _logView = null!;
    private TextField _inputField = null!;
    private Label _statusLabel = null!;
    private Label _targetLabel = null!;
    private Label _movementLabel = null!;
    private FrameView _logFrame = null!;
    private View _sidebar = null!;
    private Label _navPositionLabel = null!;
    private Label _navHeadingLabel = null!;
    private Label _ringInfoLabel = null!;
    private PositionRingView _ringView = null!;
    private FrameView _navigationFrame = null!;
    private FrameView _rosterFrame = null!;
    private ListView _entityListView = null!;
    private Button _approachButton = null!;
    private Button _withdrawButton = null!;
    private Button _examineButton = null!;
    private Button _talkButton = null!;
    private Button _greetButton = null!;
    private Button _engageToggleButton = null!;
    private readonly HashSet<string> _engagedEntities = new(StringComparer.OrdinalIgnoreCase);
    private Label _entityActionLabel = null!;
    private readonly List<NearbyEntityInfo> _entityRosterSnapshot = new();
    private MenuBar _menuBar = null!;
    private StatusBar _statusBar = null!;
    private Window _window = null!;
    private string _activeThemeName = "ember";
    private ConnectionProfile? _activeConnection;
    private string? _activeAccountName;
    private string? _activeCharacterName;
    private string? _activeWorldName;
    private bool _isConnected;
    private AppearanceSettings _defaultAppearance;
    private LogListSource _logSource = null!;
    private static readonly JsonSerializerOptions OutgoingLogOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public MudClientApp(MudClientConfig config, string configPath, string connectionsPath)
    {
        _config = config;
        _configPath = configPath;
        _connectionsPath = connectionsPath;
        _connections = _connectionStore.Load(_connectionsPath);
        _router = new MessageRouter(_state);
        _sendMode = SocketMessageModeParser.Parse(_config.SendMode, SocketMessageMode.Event);
        _receiveMode = SocketMessageModeParser.Parse(_config.ReceiveMode, SocketMessageMode.Event);
        _defaultAppearance = BuildAppearanceFromConfig(_config);
        RefreshKeyBindings();
    }

    public void Run()
    {
        Application.Init();
        BuildUi();
        WireTransport();

        Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(1), _ =>
        {
            ShowConnectionsDialog(showOnLaunch: true);
            return false;
        });

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
                new MenuItem("_Login", "", ShowLoginDialog),
                new MenuItem("C_onnections", "", () => ShowConnectionsDialog(showOnLaunch: false))
            }),
            new MenuBarItem("_View", new[]
            {
                new MenuItem("_Toggle Timestamps", "", ToggleTimestamps),
                new MenuItem("_Reload Config", "", ReloadConfig),
                new MenuItem("_Settings", "", ShowSettingsDialog)
            }),
            new MenuBarItem("_Navigation", new[]
            {
                new MenuItem("_Ring", "", () => SetNavigationStyle(NavigationRingStyle.Ring)),
                new MenuItem("_Grid", "", () => SetNavigationStyle(NavigationRingStyle.Grid))
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
        Application.Top.KeyPress += OnGlobalKeyPress;
    }

    private void BuildMainLayout(Window window)
    {
        const int sidebarWidthPercent = 32;
        const int sidebarMinWidth = 40;
        int GetSidebarWidth(int totalWidth)
        {
            if (totalWidth <= 0)
            {
                return 0;
            }

            var width = Math.Max(sidebarMinWidth, (int)Math.Round(totalWidth * (sidebarWidthPercent / 100.0)));
            return Math.Clamp(width, Math.Min(sidebarMinWidth, totalWidth), totalWidth);
        }

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

        _logFrame = new FrameView("Log")
        {
            X = 0,
            Y = Pos.Bottom(header),
            Width = Dim.Fill(),
            Height = Dim.Fill(4)
        };

        _logSource = new LogListSource(_logEntries);
        _logView = new ListView(_logSource)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _logFrame.Add(_logView);

        var inputFrame = new FrameView("Input")
        {
            X = 0,
            Y = Pos.Bottom(_logFrame),
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

        _sidebar = new View
        {
            X = 0,
            Y = Pos.Bottom(header),
            Width = sidebarMinWidth,
            Height = Dim.Fill(4)
        };

        _navigationFrame = new FrameView("Navigation")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 15
        };

        _navPositionLabel = new Label("Loc: unknown")
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1
        };

        _navHeadingLabel = new Label("Head: -- Spd: idle")
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(),
            Height = 1
        };

        var initialSpeed = NavigationSpeeds.LastOrDefault() ?? "walk";
        _ringInfoLabel = new Label($"Select: N {initialSpeed}")
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(),
            Height = 1
        };

        _ringView = new PositionRingView
        {
            X = 1,
            Y = 3,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
            RangeBands = NavigationSpeeds,
            AngleStep = 45,
            WantMousePositionReports = true
        };
        _ringView.Style = ParseNavigationRingStyle(_config.NavRingStyle);
        _ringView.SelectionConfirmed += OnRingSelectionConfirmed;
        _ringView.SelectionChanged += OnRingSelectionChanged;
        _ringView.StopRequested += OnRingStopRequested;

        _navigationFrame.Add(_navPositionLabel, _navHeadingLabel, _ringInfoLabel, _ringView);

        _rosterFrame = new FrameView("Nearby Entities")
        {
            X = 0,
            Y = Pos.Bottom(_navigationFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _entityListView = new ListView(Array.Empty<string>())
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(3)
        };
        _entityListView.SelectedItemChanged += _ => UpdateEntityActionState();

        var entityActionBar = new View
        {
            X = 0,
            Y = Pos.Bottom(_entityListView),
            Width = Dim.Fill(),
            Height = 3
        };

        _examineButton = new Button("Examine")
        {
            X = 0,
            Y = 0,
            Enabled = false
        };
        _examineButton.Clicked += () => SendEntityCommand("/examine");

        _talkButton = new Button("Talk")
        {
            X = Pos.Right(_examineButton) + 1,
            Y = 0,
            Enabled = false
        };
        _talkButton.Clicked += () => SendEntityCommand("/talk");

        _greetButton = new Button("Greet")
        {
            X = Pos.Right(_talkButton) + 1,
            Y = 0,
            Enabled = false
        };
        _greetButton.Clicked += () => SendEntityCommand("/emote waves at");

        _approachButton = new Button("Approach")
        {
            X = 0,
            Y = 1,
            Enabled = false
        };
        _approachButton.Clicked += () => _ = MoveRelativeToEntityAsync(true);

        _withdrawButton = new Button("Withdraw")
        {
            X = Pos.Right(_approachButton) + 1,
            Y = 1,
            Enabled = false
        };
        _withdrawButton.Clicked += () => _ = MoveRelativeToEntityAsync(false);

        _engageToggleButton = new Button("Engage")
        {
            X = Pos.Right(_withdrawButton) + 1,
            Y = 1,
            Enabled = false
        };
        _engageToggleButton.Clicked += ToggleEngage;

        _entityActionLabel = new Label("Select an entity")
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = 1
        };

        entityActionBar.Add(_examineButton, _talkButton, _greetButton, _approachButton, _withdrawButton, _engageToggleButton, _entityActionLabel);
        _rosterFrame.Add(_entityListView, entityActionBar);

        _sidebar.Add(_navigationFrame, _rosterFrame);

        window.Add(header, _logFrame, inputFrame, _sidebar);
        RefreshEntityRoster();
        RefreshMovementState();

        UpdateSidebarLayout(GetSidebarWidth);
        window.LayoutComplete += _ => UpdateSidebarLayout(GetSidebarWidth);
    }

    private void WireTransport()
    {
        _transport.Connected += async () =>
        {
            Application.MainLoop.Invoke(() =>
            {
                _isConnected = true;
                RefreshSessionHeader();
                AppendLogLine("Connected.");
            });
            await SendHandshakeAsync();
            await TrySendAutoAuthAsync();
        };

        _transport.Disconnected += reason =>
        {
            Application.MainLoop.Invoke(() =>
            {
                _isConnected = false;
                _activeWorldName = null;
                _activeCharacterName = null;
                RefreshSessionHeader();
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
                else if (message.Type == "world_entry")
                {
                    ApplyCharacterFromWorldEntry(message.Payload);
                }

                foreach (var line in _router.Handle(message, _config.ShowDiagnosticInfo))
                {
                    AppendLogLine(line);
                }
                RefreshTargetState();
                RefreshMovementState();
                RefreshEntityRoster();

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
            RefreshSessionHeader();
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
        if (!EnsureConnectedForSend())
        {
            return;
        }

        if (_sendMode == SocketMessageMode.Event)
        {
            LogOutgoing(type, payload);
            await _transport.SendAsync(type, payload);
            return;
        }

        var message = new OutgoingMessage(type, payload, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        LogOutgoing(_config.SendEventName, message);
        await _transport.SendAsync(_config.SendEventName, message);
    }

    private async Task SendRawAsync(string json)
    {
        try
        {
            if (!EnsureConnectedForSend())
            {
                return;
            }

            using var doc = JsonDocument.Parse(json);
            if (_sendMode == SocketMessageMode.Event)
            {
                string? eventName = null;
                JsonElement payload = doc.RootElement.Clone();
                var payloadJson = doc.RootElement.GetRawText();

                if (doc.RootElement.TryGetProperty("event", out var eventProp))
                {
                    eventName = eventProp.GetString();
                    if (doc.RootElement.TryGetProperty("payload", out var payloadProp))
                    {
                        payload = payloadProp.Clone();
                        payloadJson = payloadProp.GetRawText();
                    }
                }
                else if (doc.RootElement.TryGetProperty("type", out var typeProp))
                {
                    eventName = typeProp.GetString();
                    if (doc.RootElement.TryGetProperty("payload", out var payloadProp))
                    {
                        payload = payloadProp.Clone();
                        payloadJson = payloadProp.GetRawText();
                    }
                }

                if (string.IsNullOrWhiteSpace(eventName))
                {
                    eventName = _config.SendEventName;
                }

                LogOutgoingJson(eventName!, payloadJson);
                await _transport.SendAsync(eventName!, payload);
                return;
            }

            var rawJson = doc.RootElement.GetRawText();
            LogOutgoingJson(_config.SendEventName, rawJson);
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
            _ = SendSlashCommandAsync(text);
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

    private void OnGlobalKeyPress(View.KeyEventEventArgs args)
    {
        if (args.Handled || !IsFocusWithinMainWindow() || _inputField.HasFocus)
        {
            return;
        }

        if (TryHandleKeybind(args))
        {
            return;
        }

        if (!ShouldRouteToInput(args.KeyEvent.Key))
        {
            return;
        }

        _inputField.SetFocus();
        if (_inputField.ProcessKey(args.KeyEvent))
        {
            args.Handled = true;
        }
    }

    private bool TryHandleKeybind(View.KeyEventEventArgs args)
    {
        if (!TryNormalizeKeyEvent(args.KeyEvent, out var binding))
        {
            return false;
        }

        if (!_keybindActionsByKey.TryGetValue(binding, out var actionId))
        {
            return false;
        }

        ExecuteKeybindAction(actionId);
        args.Handled = true;
        return true;
    }

    private void ExecuteKeybindAction(string actionId)
    {
        if (!_resolvedKeyCommands.TryGetValue(actionId, out var command) ||
            string.IsNullOrWhiteSpace(command) ||
            string.Equals(command, "none", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var context = new MacroContext
        {
            TargetToken = _state.CurrentTargetToken,
            SelfToken = _activeCharacterName
        };
        var resolved = _macroEngine.Resolve(command, context).Trim();
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return;
        }

        if (resolved.StartsWith("/", StringComparison.Ordinal))
        {
            _ = SendSlashCommandAsync(resolved);
        }
        else
        {
            _ = SendTextCommandAsync(resolved);
        }
    }

    private bool IsFocusWithinMainWindow()
    {
        var focused = Application.Top?.Focused;
        while (focused != null)
        {
            if (focused == _window)
            {
                return true;
            }
            focused = focused.SuperView;
        }

        return false;
    }

    private static bool ShouldRouteToInput(Key key)
    {
        if ((key & (Key.AltMask | Key.CtrlMask)) != 0)
        {
            return false;
        }

        if (key == (Key)'/')
        {
            return true;
        }

        if (key is >= Key.A and <= Key.Z)
        {
            return true;
        }

        if (key == Key.Space)
        {
            return true;
        }

        return false;
    }


    private static string BuildBindingString(bool ctrl, bool shift, bool alt, string token)
    {
        var parts = new List<string>();
        if (ctrl)
        {
            parts.Add("Ctrl");
        }
        if (shift)
        {
            parts.Add("Shift");
        }
        if (alt)
        {
            parts.Add("Alt");
        }

        parts.Add(token);
        return string.Join("+", parts);
    }

    private static bool TryNormalizeKeyEvent(KeyEvent keyEvent, out string binding)
    {
        binding = string.Empty;
        var key = keyEvent.Key;
        if (key == Key.Esc)
        {
            return false;
        }

        var shift = (key & Key.ShiftMask) != 0;
        var ctrl = (key & Key.CtrlMask) != 0;
        var alt = (key & Key.AltMask) != 0;

        if (!TryGetKeyToken(key, shift, out var token))
        {
            return false;
        }

        binding = BuildBindingString(ctrl, shift, alt, token);
        return true;
    }

    private static bool TryGetKeyToken(Key key, bool shift, out string token)
    {
        token = string.Empty;
        var raw = key & Key.CharMask;
        if (raw == 0)
        {
            return false;
        }

        var ch = (char)raw;
        if (char.IsControl(ch))
        {
            return false;
        }

        if (shift && ShiftedKeyMap.TryGetValue(ch, out var unshifted))
        {
            ch = unshifted;
        }

        if (char.IsLetter(ch))
        {
            token = char.ToUpperInvariant(ch).ToString();
            return true;
        }

        token = ch switch
        {
            ',' => ",",
            '.' => ".",
            '-' => "-",
            '=' => "=",
            '/' => "/",
            '0' => "0",
            '1' => "1",
            '2' => "2",
            '3' => "3",
            '4' => "4",
            '5' => "5",
            '6' => "6",
            '7' => "7",
            '8' => "8",
            '9' => "9",
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(token);
    }

    private static readonly Dictionary<char, char> ShiftedKeyMap = new()
    {
        ['!'] = '1',
        ['@'] = '2',
        ['#'] = '3',
        ['$'] = '4',
        ['%'] = '5',
        ['^'] = '6',
        ['&'] = '7',
        ['*'] = '8',
        ['('] = '9',
        [')'] = '0',
        ['_'] = '-',
        ['+'] = '=',
        ['<'] = ',',
        ['>'] = '.'
    };

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

    private async Task SendSlashCommandAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (TryHandleLocalSlashCommand(text))
        {
            return;
        }

        if (_sendMode == SocketMessageMode.Event)
        {
            LogOutgoing("command", text);
            await _transport.SendAsync("command", text);
        }
        else
        {
            var payload = new { command = text, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            await SendMessageAsync("command", payload);
        }
        AppendLogLine($"> {text}");
    }

    private bool TryHandleLocalSlashCommand(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        var commandLine = trimmed[1..];
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return false;
        }

        var parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        if (string.Equals(parts[0], "client", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length == 1)
            {
                AppendLogLine("Client commands: roster");
            }
            else
            {
                HandleClientCommand(string.Join(' ', parts.Skip(1)));
            }
            AppendLogLine($"> {text}");
            return true;
        }

        if (string.Equals(parts[0], "roster", StringComparison.OrdinalIgnoreCase))
        {
            HandleClientCommand(commandLine);
            AppendLogLine($"> {text}");
            return true;
        }

        return false;
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
        var direction = HeadingToCompass(selection.AngleDegrees);
        var speed = string.IsNullOrWhiteSpace(selection.RangeBand) ? "walk" : selection.RangeBand.ToLowerInvariant();
        _ringInfoLabel.Text = $"Select: {direction} {speed}";
    }

    private void OnRingSelectionConfirmed(PositionSelection selection)
    {
        var direction = HeadingToCompass(selection.AngleDegrees);
        var speed = string.IsNullOrWhiteSpace(selection.RangeBand) ? "walk" : selection.RangeBand.ToLowerInvariant();
        _ = SendSlashCommandAsync($"/move compass:{direction} speed:{speed}");
        _inputField.SetFocus();
    }

    private void OnRingStopRequested()
    {
        _ = SendSlashCommandAsync("/stop");
        _inputField.SetFocus();
    }

    private void SetNavigationStyle(NavigationRingStyle style)
    {
        _ringView.Style = style;
        _config.NavRingStyle = style switch
        {
            NavigationRingStyle.Grid => "grid",
            _ => "ring"
        };
        _ringView.SetNeedsDisplay();
        SaveAppearanceToActiveProfile();
        SaveConfig();
        AppendLogLine($"Navigation style: {_config.NavRingStyle}");
    }

    private static NavigationRingStyle ParseNavigationRingStyle(string? value)
    {
        if (string.Equals(value, "grid", StringComparison.OrdinalIgnoreCase))
        {
            return NavigationRingStyle.Grid;
        }

        return NavigationRingStyle.Ring;
    }

    private void RefreshTargetState()
    {
        var targetToken = _state.CurrentTargetName ?? _state.CurrentTargetId;
        _targetLabel.Text = $"Target: {(string.IsNullOrWhiteSpace(targetToken) ? "none" : targetToken)}";
        _ringView.HasTarget = true;
        _ringView.SetNeedsDisplay();
    }

    private void RefreshMovementState()
    {
        var speed = string.IsNullOrWhiteSpace(_state.CurrentSpeed) ? "idle" : _state.CurrentSpeed!.ToLowerInvariant();
        var headingDegrees = _state.CurrentHeading.HasValue ? NormalizeHeading(_state.CurrentHeading.Value) : (double?)null;
        var headingCompass = headingDegrees.HasValue ? HeadingToCompass(headingDegrees.Value) : "unknown";
        var directions = _state.AvailableDirections.Count > 0
            ? $"[{string.Join("] [", _state.AvailableDirections)}]"
            : "none";

        _movementLabel.Text = $"Movement: {speed} | Facing: {headingCompass} | Available: {directions}";

        var positionText = _state.PlayerPosition.HasValue
            ? $"{_state.PlayerPosition.Value.X:0.#},{_state.PlayerPosition.Value.Y:0.#},{_state.PlayerPosition.Value.Z:0.#}"
            : "unknown";
        _navPositionLabel.Text = $"Loc: {positionText}";
        _navHeadingLabel.Text = headingDegrees.HasValue
            ? $"Head: {(int)Math.Round(headingDegrees.Value):000}deg {headingCompass} Spd: {speed}"
            : $"Head: -- Spd: {speed}";
    }

    private void RefreshSessionHeader()
    {
        var status = _isConnected ? $"connected ({_config.ServerUrl})" : "disconnected";
        var world = string.IsNullOrWhiteSpace(_activeWorldName) ? "unknown" : _activeWorldName;
        var account = string.IsNullOrWhiteSpace(_activeAccountName) ? "?" : _activeAccountName;
        var character = string.IsNullOrWhiteSpace(_activeCharacterName) ? "?" : _activeCharacterName;
        _statusLabel.Text = $"Status: {status} | World: {world} | User: {account}/{character}";
    }

    private void UpdateSidebarLayout(Func<int, int> getSidebarWidth)
    {
        if (_window == null)
        {
            return;
        }

        var totalWidth = _window.Bounds.Width;
        var sidebarWidth = getSidebarWidth(totalWidth);
        sidebarWidth = Math.Clamp(sidebarWidth, 0, totalWidth);
        var logWidth = Math.Max(0, totalWidth - sidebarWidth);

        _logFrame.X = 0;
        _logFrame.Width = logWidth;
        _sidebar.X = logWidth;
        _sidebar.Width = sidebarWidth;
    }

    private void RefreshEntityRoster()
    {
        var selectedKey = GetSelectedNearbyEntity() is NearbyEntityInfo selected
            ? BuildEngagementKey(selected)
            : null;
        var entries = BuildNearbyEntities();

        _entityRosterSnapshot.Clear();
        _entityRosterSnapshot.AddRange(entries);

        var lines = entries.Select(FormatNearbyEntityLine).ToList();
        if (lines.Count == 0)
        {
            lines.Add("No nearby entities.");
        }

        _entityListView.SetSource(lines);
        var sourceCount = _entityListView.Source.Count;
        if (entries.Count > 0)
        {
            var selectedIndex = -1;
            if (!string.IsNullOrWhiteSpace(selectedKey))
            {
                selectedIndex = entries.FindIndex(entity =>
                    string.Equals(BuildEngagementKey(entity), selectedKey, StringComparison.OrdinalIgnoreCase));
            }

            _entityListView.SelectedItem = selectedIndex >= 0
                ? selectedIndex
                : Math.Clamp(_entityListView.SelectedItem, 0, sourceCount - 1);
        }
        else
        {
            _entityListView.SelectedItem = -1;
        }

        UpdateEntityActionState();
    }

    private string FormatNearbyEntityLine(NearbyEntityInfo entity)
    {
        var name = GetEntityDisplayName(entity);
        var type = string.IsNullOrWhiteSpace(entity.Type) ? "entity" : entity.Type;
        var bearingText = entity.Bearing.HasValue
            ? $"{(int)Math.Round(NormalizeHeading(entity.Bearing.Value)):000}deg"
            : "bearing ?";
        var elevationDisplay = entity.Elevation.HasValue
            ? $"{(entity.Elevation >= 0 ? "+" : string.Empty)}{entity.Elevation:0.#}ft"
            : "elev ?";
        var rangeDisplay = FormatDistance(entity.Range);

        return $"{name} ({type}) - {bearingText} | elev {elevationDisplay} | {rangeDisplay}";
    }

    private string GetEntityDisplayName(NearbyEntityInfo entity)
    {
        return string.IsNullOrWhiteSpace(entity.Name) ? entity.Id : entity.Name;
    }

    private string FormatDistance(double? value)
    {
        return value.HasValue ? $"{value.Value:0.#}ft range" : "range ?";
    }

    private void UpdateEntityActionState()
    {
        var entity = GetSelectedNearbyEntity();
        if (entity is NearbyEntityInfo selected)
        {
            _examineButton.Enabled = true;
            _talkButton.Enabled = true;
            _greetButton.Enabled = true;
            _approachButton.Enabled = true;
            _withdrawButton.Enabled = true;
            _engageToggleButton.Enabled = true;
            UpdateEngageButtonLabel(selected);
            _entityActionLabel.Text = $"{GetEntityDisplayName(selected)} - {FormatDistance(selected.Range)}";
        }
        else
        {
            _approachButton.Enabled = false;
            _withdrawButton.Enabled = false;
            _examineButton.Enabled = false;
            _talkButton.Enabled = false;
            _greetButton.Enabled = false;
            _engageToggleButton.Enabled = false;
            _entityActionLabel.Text = "Select an entity to act on.";
        }
    }

    private NearbyEntityInfo? GetSelectedNearbyEntity()
    {
        if (_entityRosterSnapshot.Count == 0)
        {
            return null;
        }

        var index = _entityListView.SelectedItem;
        if (index < 0 || index >= _entityRosterSnapshot.Count)
        {
            return null;
        }

        return _entityRosterSnapshot[index];
    }

    private async Task MoveRelativeToEntityAsync(bool approach)
    {
        var entity = GetSelectedNearbyEntity();
        if (!entity.HasValue)
        {
            AppendLogLine("Select an entity first.");
            return;
        }

        var heading = DetermineHeadingForEntity(entity.Value, approach);
        if (!heading.HasValue)
        {
            AppendLogLine($"Bearing is unknown for {GetEntityDisplayName(entity.Value)}.");
            return;
        }

        var speed = approach ? "walk" : "run";
        var movement = new MovementCommand(speed, heading, null);
        await SendMovementCommandAsync(movement);
        var verb = approach ? "Approaching" : "Withdrawing from";
        AppendLogLine($"{verb} {GetEntityDisplayName(entity.Value)} ({heading.Value}deg).");
    }

    private void SendEntityCommand(string command)
    {
        var entity = GetSelectedNearbyEntity();
        if (!entity.HasValue)
        {
            AppendLogLine("Select an entity first.");
            return;
        }

        var token = GetEntityCommandToken(entity.Value);
        _ = SendSlashCommandAsync($"{command} {token}".Trim());
        AppendLogLine($"> {command} {token}".Trim());
    }

    private void ToggleEngage()
    {
        var entity = GetSelectedNearbyEntity();
        if (!entity.HasValue)
        {
            AppendLogLine("Select an entity first.");
            return;
        }

        var token = GetEntityCommandToken(entity.Value);
        var key = BuildEngagementKey(entity.Value);
        if (_engagedEntities.Contains(key))
        {
            _engagedEntities.Remove(key);
            _ = SendSlashCommandAsync($"/disengage {token}".Trim());
            AppendLogLine($"> /disengage {token}".Trim());
        }
        else
        {
            _engagedEntities.Add(key);
            _ = SendSlashCommandAsync($"/engage {token}".Trim());
            AppendLogLine($"> /engage {token}".Trim());
        }

        UpdateEngageButtonLabel(entity.Value);
        RefreshEntityRoster();
    }

    private void UpdateEngageButtonLabel(NearbyEntityInfo entity)
    {
        var engaged = _engagedEntities.Contains(BuildEngagementKey(entity));
        _engageToggleButton.Text = engaged ? "Disengage" : "Engage";
    }

    private string BuildEngagementKey(NearbyEntityInfo entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.Id))
        {
            return $"id:{entity.Id}";
        }

        return $"name:{GetEntityCommandToken(entity)}";
    }

    private string GetEntityCommandToken(NearbyEntityInfo entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.Name))
        {
            return entity.Name;
        }

        return entity.Id;
    }

    private int? DetermineHeadingForEntity(NearbyEntityInfo entity, bool approach)
    {
        if (!entity.Bearing.HasValue)
        {
            return null;
        }

        var heading = NormalizeHeading((int)Math.Round(entity.Bearing.Value));
        if (!approach)
        {
            heading = NormalizeHeading(heading + 180);
        }

        return heading;
    }

    private static double? CalculateBearingFromDelta(Vector3 delta)
    {
        var horizontal = Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        if (horizontal < 0.001)
        {
            return null;
        }

        var angle = Math.Atan2(delta.X, -delta.Z) * 180.0 / Math.PI;
        return NormalizeHeading(angle);
    }

    private static int NormalizeHeading(int angle)
    {
        var normalized = angle % 360;
        if (normalized < 0)
        {
            normalized += 360;
        }

        return normalized;
    }

    private static double NormalizeHeading(double angle)
    {
        var normalized = angle % 360.0;
        if (normalized < 0)
        {
            normalized += 360.0;
        }

        return normalized;
    }

    private List<NearbyEntityInfo> BuildNearbyEntities()
    {
        List<NearbyEntityInfo> raw;
        if (_state.ProximityRoster.HasEntities())
        {
            raw = _state.ProximityRoster.GetEntitiesForNavigation()
                .Select(entity => new NearbyEntityInfo(
                    entity.Id,
                    entity.Name,
                    entity.Type,
                    entity.Bearing,
                    entity.Elevation,
                    entity.Range))
                .ToList();
        }
        else
        {
            raw = _state.Entities
                .Where(e => !string.IsNullOrWhiteSpace(e.Name) || !string.IsNullOrWhiteSpace(e.Type))
                .Select(entity => new NearbyEntityInfo(
                    entity.Id,
                    entity.Name,
                    entity.Type,
                    entity.Bearing,
                    GetElevationForEntity(entity),
                    GetRangeForEntity(entity)))
                .ToList();
        }

        return DeduplicateNearbyEntities(raw)
            .OrderByDescending(entity => _engagedEntities.Contains(BuildEngagementKey(entity)))
            .ThenBy(entity => entity.Range ?? double.MaxValue)
            .ThenBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<NearbyEntityInfo> DeduplicateNearbyEntities(IEnumerable<NearbyEntityInfo> entities)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in entities)
        {
            var key = BuildEntityKey(entity);
            if (seen.Add(key))
            {
                yield return entity;
            }
        }
    }

    private static string BuildEntityKey(NearbyEntityInfo entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.Id))
        {
            return $"id:{entity.Id}";
        }

        var name = string.IsNullOrWhiteSpace(entity.Name) ? "unknown" : entity.Name.Trim();
        var type = string.IsNullOrWhiteSpace(entity.Type) ? "entity" : entity.Type.Trim();
        return $"name:{name}|type:{type}";
    }

    private double? GetElevationForEntity(EntityInfo entity)
    {
        if (entity.Elevation.HasValue)
        {
            return entity.Elevation;
        }

        if (_state.PlayerPosition.HasValue && entity.Position.HasValue)
        {
            return entity.Position.Value.Y - _state.PlayerPosition.Value.Y;
        }

        return null;
    }

    private double? GetRangeForEntity(EntityInfo entity)
    {
        if (entity.Range.HasValue)
        {
            return entity.Range;
        }

        if (_state.PlayerPosition.HasValue && entity.Position.HasValue)
        {
            return _state.PlayerPosition.Value.DistanceTo(entity.Position.Value);
        }

        return null;
    }

    private void AppendLogLine(string line)
    {
        var formatted = _showTimestamps
            ? $"[{DateTime.Now:HH:mm:ss}] {line}"
            : line;

        var kind = LogListSource.Classify(line);
        _logEntries.Add(new LogEntry(formatted, kind));
        if (_logEntries.Count > MaxLogLines)
        {
            _logEntries.RemoveRange(0, _logEntries.Count - MaxLogLines);
        }

        _logView.SetNeedsDisplay();
        ScrollLogToEnd();
    }

    private void ScrollLogToEnd()
    {
        var count = _logSource?.Count ?? 0;
        if (count <= 0)
        {
            return;
        }

        var lastIndex = count - 1;
        _logView.SelectedItem = lastIndex;

        var visibleRows = Math.Max(1, _logView.Bounds.Height);
        _logView.TopItem = Math.Max(0, lastIndex - visibleRows + 1);
    }

    private void ShowHelp()
    {
        AppendLogLine("Slash commands are forwarded to the server.");
        AppendLogLine("Use the menu or function keys for connect/login/theme/reload/quit actions.");
        AppendLogLine("Client-only: /client roster (or /roster) dumps the proximity roster cache.");
        AppendLogLine("Use macros for quick actions. Use the ring to send position intents relative to the current target.");
    }

    private void ShowLoginDialog()
    {
        var okButton = new Button("Login");
        var cancelButton = new Button("Cancel");
        okButton.IsDefault = true;
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
                    _activeAccountName = GetFieldText(guestField);
                    RefreshSessionHeader();
                    _ = SendMessageAsync("auth", new { method = "guest", guestName = GetFieldText(guestField) });
                    AppendLogLine("Auth: guest request sent.");
                    break;
                case 1:
                    if (string.IsNullOrWhiteSpace(GetFieldText(tokenField)))
                    {
                        MessageBox.ErrorQuery("Auth", "Token required.", "Ok");
                        return;
                    }
                    _activeAccountName = GetFieldText(tokenField);
                    RefreshSessionHeader();
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
                    _activeAccountName = GetFieldText(userField);
                    RefreshSessionHeader();
                    _ = SendMessageAsync("auth", new { method = "credentials", username = GetFieldText(userField), password = GetFieldText(passField) });
                    AppendLogLine("Auth: credentials request sent.");
                    break;
            }

            Application.RequestStop();
        };

        cancelButton.Clicked += () => Application.RequestStop();

        Application.Run(dialog);
    }

    private void ShowConnectionsDialog(bool showOnLaunch)
    {
        var connectButton = new Button("Connect");
        var newButton = new Button("New");
        var editButton = new Button("Edit");
        var deleteButton = new Button("Delete");
        var closeButton = new Button("Back");
        connectButton.IsDefault = true;

        var dialog = new Dialog("Connections", 70, 20, connectButton, newButton, editButton, deleteButton, closeButton);

        var listItems = _connections.Connections
            .Select(connection => $"{connection.Name} ({connection.Host}:{connection.Port})")
            .ToList();
        var listView = new ListView(listItems)
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(2),
            Height = 8
        };

        var preferredToggle = new CheckBox(1, 10, "Preferred on launch");
        var autoConnectToggle = new CheckBox(1, 11, "Auto-connect preferred")
        {
            Checked = _config.AutoConnect
        };
        var statusLabel = new Label(string.Empty)
        {
            X = 1,
            Y = 13,
            Width = Dim.Fill(2)
        };

        dialog.Add(listView, preferredToggle, autoConnectToggle, statusLabel);

        void RefreshList()
        {
            listItems = _connections.Connections
                .Select(connection => $"{connection.Name} ({connection.Host}:{connection.Port})")
                .ToList();
            listView.SetSource(listItems);
            if (listItems.Count == 0)
            {
                listView.SelectedItem = -1;
            }
            else if (listView.SelectedItem < 0)
            {
                listView.SelectedItem = 0;
            }
        }

        void UpdatePreferredToggle()
        {
            var connection = GetSelectedConnection(listView);
            if (connection == null)
            {
                preferredToggle.Checked = false;
                return;
            }

            preferredToggle.Checked = string.Equals(connection.Id, _connections.PreferredConnectionId, StringComparison.OrdinalIgnoreCase);
        }

        listView.SelectedItemChanged += _ => UpdatePreferredToggle();

        preferredToggle.Toggled += _ =>
        {
            var connection = GetSelectedConnection(listView);
            if (connection == null)
            {
                preferredToggle.Checked = false;
                return;
            }

            _connections.PreferredConnectionId = preferredToggle.Checked ? connection.Id : null;
            SaveConnections();
        };

        autoConnectToggle.Toggled += _ =>
        {
            _config.AutoConnect = autoConnectToggle.Checked;
            SaveConfig();
        };

        connectButton.Clicked += () =>
        {
            var connection = GetSelectedConnection(listView);
            if (connection == null)
            {
                statusLabel.Text = "Select a connection.";
                return;
            }

            _connections.PreferredConnectionId = connection.Id;
            SaveConnections();
            ConnectToConnection(connection);
            statusLabel.Text = $"Connecting to {connection.Name}...";
            Application.RequestStop();
        };

        newButton.Clicked += () =>
        {
            var connection = ShowConnectionEditor(null);
            if (connection == null)
            {
                return;
            }

            _connections.Connections.Add(connection);
            SaveConnections();
            RefreshList();
        };

        editButton.Clicked += () =>
        {
            var connection = GetSelectedConnection(listView);
            if (connection == null)
            {
                statusLabel.Text = "Select a connection to edit.";
                return;
            }

            var updated = ShowConnectionEditor(connection);
            if (updated == null)
            {
                return;
            }

            var index = _connections.Connections.FindIndex(c => string.Equals(c.Id, connection.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _connections.Connections[index] = updated;
                SaveConnections();
                RefreshList();
            }
        };

        deleteButton.Clicked += () =>
        {
            var connection = GetSelectedConnection(listView);
            if (connection == null)
            {
                statusLabel.Text = "Select a connection to delete.";
                return;
            }

            var confirm = MessageBox.Query("Delete", $"Delete {connection.Name}?", "Yes", "No");
            if (confirm != 0)
            {
                return;
            }

            _connections.Connections.Remove(connection);
            if (string.Equals(_connections.PreferredConnectionId, connection.Id, StringComparison.OrdinalIgnoreCase))
            {
                _connections.PreferredConnectionId = null;
            }
            SaveConnections();
            RefreshList();
            UpdatePreferredToggle();
        };

        closeButton.Clicked += () => Application.RequestStop();

        RefreshList();
        UpdatePreferredToggle();

        if (showOnLaunch && _config.AutoConnect)
        {
            var preferred = GetPreferredConnection();
            if (preferred != null)
            {
                listView.SelectedItem = _connections.Connections.FindIndex(c => c.Id == preferred.Id);
                preferredToggle.Checked = true;
                Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(1), _ =>
                {
                    ConnectToConnection(preferred);
                    statusLabel.Text = $"Connecting to {preferred.Name}...";
                    Application.RequestStop();
                    return false;
                });
            }
        }

        Application.Run(dialog);
    }

    private ConnectionProfile? ShowConnectionEditor(ConnectionProfile? existing)
    {
        var okButton = new Button(existing == null ? "Create" : "Save");
        var cancelButton = new Button("Cancel");
        okButton.IsDefault = true;

        var dialog = new Dialog(existing == null ? "New Connection" : "Edit Connection", 70, 22, okButton, cancelButton);

        var nameLabel = new Label("Name:")
        {
            X = 1,
            Y = 1
        };
        var nameField = new TextField(existing?.Name ?? string.Empty)
        {
            X = 12,
            Y = 1,
            Width = 40
        };

        var hostLabel = new Label("Host:")
        {
            X = 1,
            Y = 3
        };
        var hostField = new TextField(existing?.Host ?? "localhost")
        {
            X = 12,
            Y = 3,
            Width = 40
        };

        var portLabel = new Label("Port:")
        {
            X = 1,
            Y = 5
        };
        var portField = new TextField(existing?.Port.ToString() ?? "3100")
        {
            X = 12,
            Y = 5,
            Width = 10
        };

        var tlsToggle = new CheckBox(1, 7, "Use TLS")
        {
            Checked = existing?.UseTls ?? false
        };

        var authLabel = new Label("Auth:")
        {
            X = 1,
            Y = 9
        };
        var authOptions = new[] { (ustring)"Guest", "Token", "Creds" };
        var authIndex = existing?.AuthMethod?.ToLowerInvariant() switch
        {
            "token" => 1,
            "creds" => 2,
            _ => 0
        };
        var authGroup = new RadioGroup(12, 9, authOptions, authIndex);

        var accountLabel = new Label("Account:")
        {
            X = 1,
            Y = 13
        };
        var accountField = new TextField(existing?.AccountName ?? string.Empty)
        {
            X = 12,
            Y = 13,
            Width = 40
        };

        var passwordLabel = new Label("Password:")
        {
            X = 1,
            Y = 15
        };
        var passwordField = new TextField(existing?.Password ?? string.Empty)
        {
            X = 12,
            Y = 15,
            Width = 40,
            Secret = true
        };

        var characterLabel = new Label("Character:")
        {
            X = 1,
            Y = 17
        };
        var characterField = new TextField(existing?.CharacterName ?? string.Empty)
        {
            X = 12,
            Y = 17,
            Width = 40
        };

        dialog.Add(
            nameLabel, nameField,
            hostLabel, hostField,
            portLabel, portField,
            tlsToggle,
            authLabel, authGroup,
            accountLabel, accountField,
            passwordLabel, passwordField,
            characterLabel, characterField);

        ConnectionProfile? result = null;

        okButton.Clicked += () =>
        {
            var name = GetFieldText(nameField).Trim();
            var host = GetFieldText(hostField).Trim();
            var portText = GetFieldText(portField).Trim();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(host))
            {
                MessageBox.ErrorQuery("Connection", "Name and host are required.", "Ok");
                return;
            }

            if (!int.TryParse(portText, out var port))
            {
                MessageBox.ErrorQuery("Connection", "Port must be a number.", "Ok");
                return;
            }

            result = existing ?? new ConnectionProfile();
            result.Name = name;
            result.Host = host;
            result.Port = port;
            result.UseTls = tlsToggle.Checked;
            result.AccountName = GetFieldText(accountField).Trim();
            result.Password = GetFieldText(passwordField);
            result.CharacterName = GetFieldText(characterField).Trim();
            result.AuthMethod = authGroup.SelectedItem switch
            {
                1 => "token",
                2 => "creds",
                _ => "guest"
            };
            Application.RequestStop();
        };

        cancelButton.Clicked += () => Application.RequestStop();

        Application.Run(dialog);
        return result;
    }

    private ConnectionProfile? GetSelectedConnection(ListView listView)
    {
        if (listView.SelectedItem < 0 || listView.SelectedItem >= _connections.Connections.Count)
        {
            return null;
        }

        return _connections.Connections[listView.SelectedItem];
    }

    private ConnectionProfile? GetPreferredConnection()
    {
        if (string.IsNullOrWhiteSpace(_connections.PreferredConnectionId))
        {
            return null;
        }

        return _connections.Connections.FirstOrDefault(connection =>
            string.Equals(connection.Id, _connections.PreferredConnectionId, StringComparison.OrdinalIgnoreCase));
    }

    private void ConnectToConnection(ConnectionProfile connection)
    {
        _activeConnection = connection;
        _activeAccountName = connection.AccountName;
        _activeCharacterName = string.IsNullOrWhiteSpace(connection.CharacterName) ? null : connection.CharacterName;
        _config.ServerUrl = connection.BuildUrl();
        ApplyAppearanceForActiveProfile();
        RefreshSessionHeader();
        _ = ConnectAsync(_config.ServerUrl);
    }

    private async Task TrySendAutoAuthAsync()
    {
        if (_activeConnection == null)
        {
            return;
        }

        var account = _activeConnection.AccountName;
        if (string.IsNullOrWhiteSpace(account))
        {
            Application.MainLoop.Invoke(ShowLoginDialog);
            return;
        }

        _activeAccountName = account;
        RefreshSessionHeader();

        switch (_activeConnection.AuthMethod.ToLowerInvariant())
        {
            case "token":
                await SendMessageAsync("auth", new { method = "token", token = account });
                AppendLogLine("Auth: token request sent.");
                break;
            case "creds":
                if (string.IsNullOrWhiteSpace(_activeConnection.Password))
                {
                    AppendLogLine("Auth: credentials missing password.");
                    break;
                }
                await SendMessageAsync("auth", new { method = "credentials", username = account, password = _activeConnection.Password });
                AppendLogLine("Auth: credentials request sent.");
                break;
            default:
                await SendMessageAsync("auth", new { method = "guest", guestName = account });
                AppendLogLine("Auth: guest request sent.");
                break;
        }
    }

    private void ShowSettingsDialog()
    {
        var saveButton = new Button("Save");
        var cancelButton = new Button("Cancel");
        saveButton.IsDefault = true;
        var dialog = new Dialog("Settings", 90, 24, saveButton, cancelButton);

        var themeLabel = new Label("Theme:")
        {
            X = 1,
            Y = 1
        };
        var themes = GetAvailableThemes().ToList();
        var themeList = new ListView(themes)
        {
            X = 12,
            Y = 1,
            Width = 20,
            Height = 5
        };

        var navStyleLabel = new Label("Nav style:")
        {
            X = 1,
            Y = 7
        };
        var navStyleGroup = new RadioGroup(12, 7, new[] { (ustring)"Ring", "Grid" }, _ringView.Style == NavigationRingStyle.Grid ? 1 : 0);

        var navFgLabel = new Label("Nav fg:")
        {
            X = 1,
            Y = 11
        };
        var navFgField = new TextField(_config.NavRingTheme.Foreground ?? string.Empty)
        {
            X = 12,
            Y = 11,
            Width = 20
        };

        var navBgLabel = new Label("Nav bg:")
        {
            X = 1,
            Y = 13
        };
        var navBgField = new TextField(_config.NavRingTheme.Background ?? string.Empty)
        {
            X = 12,
            Y = 13,
            Width = 20
        };

        var keybindFrame = new FrameView("Keybinds")
        {
            X = 35,
            Y = 1,
            Width = Dim.Fill(2),
            Height = Dim.Fill(2)
        };

        var scopeLabel = new Label("Scope:")
        {
            X = 1,
            Y = 0
        };
        var scopeGroup = new RadioGroup(9, 0, new[] { (ustring)"Global", "Connection", "Character" }, 0);
        var scopeNote = new Label("Blank inherits. Use 'none' to disable.")
        {
            X = 1,
            Y = 1
        };

        var actionList = new ListView(KeybindActions.Select(action => action.Label).ToList())
        {
            X = 1,
            Y = 3,
            Width = 20,
            Height = 9
        };

        var bindingLabel = new Label("Binding:")
        {
            X = 23,
            Y = 3
        };
        var bindingField = new TextField(string.Empty)
        {
            X = 32,
            Y = 3,
            Width = 12
        };
        var bindButton = new Button("Bind")
        {
            X = 45,
            Y = 3
        };
        var bindHint = new Label(string.Empty)
        {
            X = 23,
            Y = 4,
            Width = Dim.Fill(2)
        };

        var commandLabel = new Label("Command:")
        {
            X = 23,
            Y = 6
        };
        var commandField = new TextField(string.Empty)
        {
            X = 32,
            Y = 6,
            Width = 32
        };

        keybindFrame.Add(scopeLabel, scopeGroup, scopeNote, actionList, bindingLabel, bindingField, bindButton, bindHint, commandLabel, commandField);

        dialog.Add(themeLabel, themeList, navStyleLabel, navStyleGroup, navFgLabel, navFgField, navBgLabel, navBgField, keybindFrame);

        var selectedThemeIndex = themes.FindIndex(name => string.Equals(name, _config.Theme, StringComparison.OrdinalIgnoreCase));
        themeList.SelectedItem = selectedThemeIndex < 0 ? 0 : selectedThemeIndex;

        KeybindScope GetSelectedScope() => scopeGroup.SelectedItem switch
        {
            1 => KeybindScope.Connection,
            2 => KeybindScope.Character,
            _ => KeybindScope.Global
        };

        var globalBindings = new Dictionary<string, string>(_config.KeyBindings.Bindings, StringComparer.OrdinalIgnoreCase);
        var globalCommands = new Dictionary<string, string>(_config.KeyBindings.Commands, StringComparer.OrdinalIgnoreCase);
        var connectionBindings = _activeConnection?.Settings.KeyBindings?.Bindings == null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(_activeConnection.Settings.KeyBindings.Bindings, StringComparer.OrdinalIgnoreCase);
        var connectionCommands = _activeConnection?.Settings.KeyBindings?.Commands == null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(_activeConnection.Settings.KeyBindings.Commands, StringComparer.OrdinalIgnoreCase);
        var characterBindings = GetCharacterKeybindOverrides() ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var characterCommands = GetCharacterKeybindCommandOverrides() ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, string> GetWorkingBindings(KeybindScope scope) => scope switch
        {
            KeybindScope.Connection => connectionBindings,
            KeybindScope.Character => characterBindings,
            _ => globalBindings
        };

        Dictionary<string, string> GetWorkingCommands(KeybindScope scope) => scope switch
        {
            KeybindScope.Connection => connectionCommands,
            KeybindScope.Character => characterCommands,
            _ => globalCommands
        };

        string? currentActionId = null;
        bool isCapturing = false;

        void CommitCurrentActionFields()
        {
            if (string.IsNullOrWhiteSpace(currentActionId))
            {
                return;
            }

            var scope = GetSelectedScope();
            var bindings = GetWorkingBindings(scope);
            var commands = GetWorkingCommands(scope);

            var bindingText = GetFieldText(bindingField).Trim();
            if (string.IsNullOrWhiteSpace(bindingText))
            {
                bindings.Remove(currentActionId);
            }
            else
            {
                bindings[currentActionId] = bindingText;
            }

            var commandText = GetFieldText(commandField).Trim();
            if (string.IsNullOrWhiteSpace(commandText))
            {
                commands.Remove(currentActionId);
            }
            else
            {
                commands[currentActionId] = commandText;
            }
        }

        void LoadActionFields(string actionId)
        {
            var scope = GetSelectedScope();
            var bindings = GetWorkingBindings(scope);
            var commands = GetWorkingCommands(scope);

            bindingField.Text = bindings.TryGetValue(actionId, out var binding) ? binding : string.Empty;
            commandField.Text = commands.TryGetValue(actionId, out var command) ? command : string.Empty;
            currentActionId = actionId;
        }

        void RefreshActionSelection()
        {
            if (actionList.Source.Count == 0)
            {
                return;
            }

            if (actionList.SelectedItem < 0)
            {
                actionList.SelectedItem = 0;
            }

            var action = KeybindActions.ElementAtOrDefault(actionList.SelectedItem);
            if (action != null)
            {
                LoadActionFields(action.Id);
            }
        }

        scopeGroup.SelectedItemChanged += _ =>
        {
            CommitCurrentActionFields();
            LoadActionFields(currentActionId ?? KeybindActions.First().Id);
        };

        actionList.SelectedItemChanged += _ =>
        {
            CommitCurrentActionFields();
            var action = KeybindActions.ElementAtOrDefault(actionList.SelectedItem);
            if (action != null)
            {
                LoadActionFields(action.Id);
            }
        };

        bindButton.Clicked += () =>
        {
            isCapturing = true;
            bindHint.Text = "Press a key (Esc to cancel).";
            dialog.SetNeedsDisplay();
        };

        dialog.KeyPress += args =>
        {
            if (!isCapturing)
            {
                return;
            }

            if (args.KeyEvent.Key == Key.Esc)
            {
                isCapturing = false;
                bindHint.Text = string.Empty;
                args.Handled = true;
                return;
            }

            if (TryNormalizeKeyEvent(args.KeyEvent, out var binding))
            {
                bindingField.Text = binding;
                isCapturing = false;
                bindHint.Text = string.Empty;
                args.Handled = true;
            }
        };

        RefreshActionSelection();

        saveButton.Clicked += () =>
        {
            CommitCurrentActionFields();

            var theme = themes.ElementAtOrDefault(themeList.SelectedItem) ?? _config.Theme;
            _config.Theme = theme;
            _config.NavRingStyle = navStyleGroup.SelectedItem == 1 ? "grid" : "ring";
            _config.NavRingTheme.Foreground = GetFieldText(navFgField).Trim();
            _config.NavRingTheme.Background = GetFieldText(navBgField).Trim();
            _ringView.Style = ParseNavigationRingStyle(_config.NavRingStyle);
            ApplyTheme(_config.Theme, logChange: true);

            var scope = GetSelectedScope();
            if (!TrySaveKeybindScope(scope, GetWorkingBindings(scope), GetWorkingCommands(scope)))
            {
                return;
            }

            SaveAppearanceToActiveProfile();
            SaveConfig();
            RefreshKeyBindings();
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
        var actionButton = new Button("Choose");
        var backButton = new Button("Back");
        actionButton.IsDefault = true;

        var dialog = new Dialog("Character", 70, 20, actionButton, backButton);

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

        var nameLabel = new Label("Name:")
        {
            X = 1,
            Y = 11
        };
        var nameField = new TextField(string.Empty)
        {
            X = 8,
            Y = 11,
            Width = 30
        };

        dialog.Add(listLabel, listView, nameLabel, nameField);
        nameField.SetFocus();

        void UpdateActionState()
        {
            var name = GetFieldText(nameField).Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                if (TryFindCharacter(name, out _))
                {
                    actionButton.Text = "Choose";
                    actionButton.Enabled = true;
                }
                else
                {
                    actionButton.Text = "Create";
                    actionButton.Enabled = _canCreateCharacter;
                }
                return;
            }

            actionButton.Text = "Choose";
            actionButton.Enabled = _characters.Count > 0 && listView.SelectedItem >= 0;
        }

        listView.SelectedItemChanged += _ => UpdateActionState();
        nameField.TextChanged += _ => UpdateActionState();
        UpdateActionState();

        actionButton.Clicked += () =>
        {
            var name = GetFieldText(nameField).Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                if (TryFindCharacter(name, out var match))
                {
                    _activeCharacterName = match!.Name;
                    ApplyAppearanceForActiveProfile();
                    _ = SendMessageAsync("character_select", new { characterId = match!.Id });
                    AppendLogLine($"Character select: {match!.Name}");
                    Application.RequestStop();
                    return;
                }

                if (!_canCreateCharacter)
                {
                    MessageBox.ErrorQuery("Create", "Character creation is disabled.", "Ok");
                    return;
                }

                _activeCharacterName = name;
                ApplyAppearanceForActiveProfile();
                _ = SendMessageAsync("character_create", new { name, appearance = new { description = "TBD" } });
                AppendLogLine($"Character create: {name}");
                Application.RequestStop();
                return;
            }

            if (_characters.Count == 0 || listView.SelectedItem < 0)
            {
                MessageBox.ErrorQuery("Select", "Choose a character or enter a name.", "Ok");
                return;
            }

            var selected = _characters[Math.Min(listView.SelectedItem, _characters.Count - 1)];
            _activeCharacterName = selected.Name;
            ApplyAppearanceForActiveProfile();
            _ = SendMessageAsync("character_select", new { characterId = selected.Id });
            AppendLogLine($"Character select: {selected.Name}");
            Application.RequestStop();
        };

        backButton.Clicked += () => Application.RequestStop();

        Application.Run(dialog);
    }

    private bool TryFindCharacter(string name, out CharacterSummary? match)
    {
        match = _characters.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        return match != null;
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


    private void LogOutgoing(string eventName, object payload)
    {
        if (!_config.ShowDiagnosticInfo)
        {
            return;
        }

        string body;
        try
        {
            body = JsonSerializer.Serialize(payload, OutgoingLogOptions);
        }
        catch
        {
            body = payload?.ToString() ?? "null";
        }

        AppendLogLine($">>> {eventName}: {body}");
    }

    private void LogOutgoingJson(string eventName, string payloadJson)
    {
        if (!_config.ShowDiagnosticInfo)
        {
            return;
        }

        AppendLogLine($">>> {eventName}: {payloadJson}");
    }

    private bool EnsureConnectedForSend()
    {
        if (_transport.IsConnected)
        {
            return true;
        }

        if (_config.ShowDiagnosticInfo)
        {
            AppendLogLine("Send skipped: socket not connected.");
        }

        return false;
    }

    private async Task SendMovementCommandAsync(MovementCommand movement)
    {
        if (string.Equals(movement.Speed, "stop", StringComparison.OrdinalIgnoreCase))
        {
            await SendSlashCommandAsync("/stop");
            return;
        }

        if (movement.Compass != null)
        {
            await SendSlashCommandAsync($"/move compass:{movement.Compass} speed:{movement.Speed}");
            return;
        }

        if (movement.Heading.HasValue)
        {
            await SendSlashCommandAsync($"/move heading:{movement.Heading.Value} speed:{movement.Speed}");
            return;
        }

        await SendSlashCommandAsync($"/move speed:{movement.Speed}");
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
    private readonly record struct NearbyEntityInfo(
        string Id,
        string Name,
        string Type,
        double? Bearing,
        double? Elevation,
        double? Range);
    private sealed record KeybindAction(string Id, string Label, string DefaultBinding, string DefaultCommand);
    private enum KeybindScope
    {
        Global,
        Connection,
        Character
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
        _config.Theme = updated.Theme;
        _config.NavRingStyle = updated.NavRingStyle;
        _config.NavRingTheme = updated.NavRingTheme;
        _config.CustomTheme = updated.CustomTheme;
        _config.PositionCommandTemplate = updated.PositionCommandTemplate;
        _config.RangeBands = updated.RangeBands;
        _config.Macros = updated.Macros;
        _config.ShowDiagnosticInfo = updated.ShowDiagnosticInfo;
        _config.KeyBindings = updated.KeyBindings ?? KeybindSettings.CreateDefaults();

        _sendMode = SocketMessageModeParser.Parse(_config.SendMode, SocketMessageMode.Event);
        _receiveMode = SocketMessageModeParser.Parse(_config.ReceiveMode, SocketMessageMode.Event);

        _ringView.RangeBands = NavigationSpeeds;
        _ringView.Style = ParseNavigationRingStyle(_config.NavRingStyle);
        _defaultAppearance = BuildAppearanceFromConfig(_config);
        ApplyTheme(_config.Theme, logChange: true);
        RefreshKeyBindings();
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
            case "roster":
                DumpProximityRoster();
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

    private void DumpProximityRoster()
    {
        var roster = _state.ProximityRoster;
        var danger = roster.DangerState.HasValue ? (roster.DangerState.Value ? "true" : "false") : "unknown";
        AppendLogLine($"Roster cache: danger={danger}");

        var channels = roster.GetChannelSnapshots();
        if (channels.Count == 0)
        {
            AppendLogLine("Roster cache is empty.");
            return;
        }

        foreach (var channel in channels)
        {
            var sampleText = channel.Sample == null
                ? "none"
                : channel.Sample.Count == 0 ? "[]" : string.Join(", ", channel.Sample);
            var lastSpeakerText = string.IsNullOrWhiteSpace(channel.LastSpeaker) ? "none" : channel.LastSpeaker;
            AppendLogLine($"Channel {channel.Name}: count={channel.Count} entities={channel.Entities.Count} sample={sampleText} lastSpeaker={lastSpeakerText}");

            foreach (var entity in channel.Entities)
            {
                var name = string.IsNullOrWhiteSpace(entity.Name) ? entity.Id : entity.Name;
                var bearing = entity.Bearing.ToString("0.##", CultureInfo.InvariantCulture);
                var elevation = entity.Elevation.ToString("0.##", CultureInfo.InvariantCulture);
                var range = entity.Range.ToString("0.##", CultureInfo.InvariantCulture);
                AppendLogLine($"- {name} ({entity.Type}) id={entity.Id} brg={bearing} elev={elevation} rng={range}");
            }
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
        var ringScheme = ResolveNavRingScheme(scheme, _config.NavRingTheme);
        _activeThemeName = resolvedName;
        _config.Theme = resolvedName;

        Application.Top.ColorScheme = scheme;
        _menuBar.ColorScheme = scheme;
        _statusBar.ColorScheme = scheme;
        _window.ColorScheme = scheme;
        _logView.ColorScheme = scheme;
        _logSource.UpdatePalette(scheme);
        _inputField.ColorScheme = scheme;
        _navigationFrame.ColorScheme = scheme;
        _ringView.ColorScheme = ringScheme;
        _statusLabel.ColorScheme = scheme;
        _targetLabel.ColorScheme = scheme;
        _movementLabel.ColorScheme = scheme;
        _navPositionLabel.ColorScheme = scheme;
        _navHeadingLabel.ColorScheme = scheme;
        _ringInfoLabel.ColorScheme = scheme;
        _rosterFrame.ColorScheme = scheme;
        _entityListView.ColorScheme = scheme;
            _approachButton.ColorScheme = scheme;
        _withdrawButton.ColorScheme = scheme;
        _entityActionLabel.ColorScheme = scheme;
        _examineButton.ColorScheme = scheme;
        _talkButton.ColorScheme = scheme;
        _greetButton.ColorScheme = scheme;
        _engageToggleButton.ColorScheme = scheme;

        Application.Top.SetNeedsDisplay();
        _window.SetNeedsDisplay();

        if (logChange)
        {
            AppendLogLine($"Theme set to {_activeThemeName}.");
        }
    }

    private static ColorScheme ResolveNavRingScheme(ColorScheme baseScheme, NavRingThemeConfig? navTheme)
    {
        if (navTheme == null ||
            (string.IsNullOrWhiteSpace(navTheme.Foreground) && string.IsNullOrWhiteSpace(navTheme.Background)))
        {
            return baseScheme;
        }

        var fg = baseScheme.Normal.Foreground;
        var bg = baseScheme.Normal.Background;

        if (!string.IsNullOrWhiteSpace(navTheme.Foreground) &&
            ColorParser.TryParse(navTheme.Foreground, out var parsedForeground))
        {
            fg = parsedForeground;
        }

        if (!string.IsNullOrWhiteSpace(navTheme.Background) &&
            ColorParser.TryParse(navTheme.Background, out var parsedBackground))
        {
            bg = parsedBackground;
        }

        var normal = new Terminal.Gui.Attribute(fg, bg);
        return new ColorScheme
        {
            Normal = normal,
            Focus = normal,
            HotNormal = normal,
            HotFocus = normal,
            Disabled = normal
        };
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

        if (TryAutoSelectCharacter())
        {
            _pendingCharacterDialog = false;
            return;
        }

        _pendingCharacterDialog = true;
    }

    private bool TryAutoSelectCharacter()
    {
        if (_activeConnection == null || string.IsNullOrWhiteSpace(_activeConnection.CharacterName))
        {
            return false;
        }

        var target = _activeConnection.CharacterName.Trim();
        var match = _characters.FirstOrDefault(character =>
            string.Equals(character.Name, target, StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            _activeCharacterName = match.Name;
            ApplyAppearanceForActiveProfile();
            _ = SendMessageAsync("character_select", new { characterId = match.Id });
            AppendLogLine($"Character select: {match.Name}");
            return true;
        }

        if (!_canCreateCharacter)
        {
            AppendLogLine($"Character \"{target}\" not found.");
            return false;
        }

        _activeCharacterName = target;
        ApplyAppearanceForActiveProfile();
        _ = SendMessageAsync("character_create", new { name = target, appearance = new { description = "TBD" } });
        AppendLogLine($"Character create: {target}");
        return true;
    }

    private void ApplyCharacterFromWorldEntry(JsonElement payload)
    {
        if (payload.TryGetProperty("zone", out var zone) && zone.ValueKind == JsonValueKind.Object)
        {
            var zoneName = zone.GetPropertyOrDefault("name")?.GetString();
            if (!string.IsNullOrWhiteSpace(zoneName))
            {
                _activeWorldName = zoneName;
                RefreshSessionHeader();
            }
        }

        if (!payload.TryGetProperty("character", out var character) ||
            character.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var name = character.GetPropertyOrDefault("name")?.GetString();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (string.Equals(_activeCharacterName, name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _activeCharacterName = name;
        ApplyAppearanceForActiveProfile();
        RefreshSessionHeader();
    }

    private AppearanceSettings BuildAppearanceFromConfig(MudClientConfig config)
    {
        return new AppearanceSettings
        {
            Theme = config.Theme,
            CustomTheme = CloneThemeConfig(config.CustomTheme),
            NavRingStyle = config.NavRingStyle,
            NavRingTheme = CloneNavRingTheme(config.NavRingTheme)
        };
    }

    private static ThemeConfig CloneThemeConfig(ThemeConfig? source)
    {
        if (source == null)
        {
            return new ThemeConfig();
        }

        return new ThemeConfig
        {
            NormalForeground = source.NormalForeground,
            NormalBackground = source.NormalBackground,
            AccentForeground = source.AccentForeground,
            AccentBackground = source.AccentBackground,
            MutedForeground = source.MutedForeground,
            MutedBackground = source.MutedBackground
        };
    }

    private static NavRingThemeConfig CloneNavRingTheme(NavRingThemeConfig? source)
    {
        if (source == null)
        {
            return new NavRingThemeConfig();
        }

        return new NavRingThemeConfig
        {
            Foreground = source.Foreground,
            Background = source.Background
        };
    }

    private AppearanceSettings CaptureCurrentAppearance()
    {
        return new AppearanceSettings
        {
            Theme = _config.Theme,
            CustomTheme = CloneThemeConfig(_config.CustomTheme),
            NavRingStyle = _config.NavRingStyle,
            NavRingTheme = CloneNavRingTheme(_config.NavRingTheme)
        };
    }

    private void ApplyAppearanceForActiveProfile()
    {
        var appearance = new AppearanceSettings
        {
            Theme = _defaultAppearance.Theme,
            CustomTheme = CloneThemeConfig(_defaultAppearance.CustomTheme),
            NavRingStyle = _defaultAppearance.NavRingStyle,
            NavRingTheme = CloneNavRingTheme(_defaultAppearance.NavRingTheme)
        };

        if (_activeConnection != null)
        {
            ApplyAppearanceOverrides(appearance, _activeConnection.Settings);

            if (!string.IsNullOrWhiteSpace(_activeCharacterName) &&
                _activeConnection.CharacterSettings.TryGetValue(_activeCharacterName, out var characterSettings))
            {
                ApplyAppearanceOverrides(appearance, characterSettings);
            }
        }

        ApplyAppearanceSettings(appearance);
        RefreshKeyBindings();
    }

    private static void ApplyAppearanceOverrides(AppearanceSettings target, AppearanceSettings? overrides)
    {
        if (overrides == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(overrides.Theme))
        {
            target.Theme = overrides.Theme;
        }

        if (overrides.CustomTheme != null)
        {
            target.CustomTheme = CloneThemeConfig(overrides.CustomTheme);
        }

        if (!string.IsNullOrWhiteSpace(overrides.NavRingStyle))
        {
            target.NavRingStyle = overrides.NavRingStyle;
        }

        if (overrides.NavRingTheme != null)
        {
            target.NavRingTheme = CloneNavRingTheme(overrides.NavRingTheme);
        }
    }

    private void ApplyAppearanceSettings(AppearanceSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Theme))
        {
            _config.Theme = settings.Theme;
        }

        if (settings.CustomTheme != null)
        {
            _config.CustomTheme = CloneThemeConfig(settings.CustomTheme);
        }

        if (!string.IsNullOrWhiteSpace(settings.NavRingStyle))
        {
            _config.NavRingStyle = settings.NavRingStyle;
        }

        if (settings.NavRingTheme != null)
        {
            _config.NavRingTheme = CloneNavRingTheme(settings.NavRingTheme);
        }

        _ringView.Style = ParseNavigationRingStyle(_config.NavRingStyle);
        ApplyTheme(_config.Theme, logChange: false);
        SaveConfig();
        RefreshKeyBindings();
    }

    private void SaveConfig()
    {
        _config.Save(_configPath);
    }

    private void SaveConnections()
    {
        _connectionStore.Save(_connectionsPath, _connections);
    }

    private void SaveAppearanceToActiveProfile()
    {
        if (_activeConnection == null)
        {
            return;
        }

        var appearance = CaptureCurrentAppearance();
        if (!string.IsNullOrWhiteSpace(_activeCharacterName))
        {
            if (_activeConnection.CharacterSettings.TryGetValue(_activeCharacterName, out var existing) &&
                existing.KeyBindings != null)
            {
                appearance.KeyBindings = CloneKeybindSettings(existing.KeyBindings);
            }
            _activeConnection.CharacterSettings[_activeCharacterName] = appearance;
        }
        else
        {
            if (_activeConnection.Settings.KeyBindings != null)
            {
                appearance.KeyBindings = CloneKeybindSettings(_activeConnection.Settings.KeyBindings);
            }
            _activeConnection.Settings = appearance;
        }

        SaveConnections();
    }

    private void RefreshKeyBindings()
    {
        _resolvedKeyBindings.Clear();
        _keybindActionsByKey.Clear();
        _resolvedKeyCommands.Clear();

        foreach (var action in KeybindActions)
        {
            var binding = ResolveBindingForAction(action);
            if (string.IsNullOrWhiteSpace(binding))
            {
                continue;
            }

            var normalized = NormalizeBindingString(binding);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            _resolvedKeyBindings[action.Id] = normalized;
            if (!_keybindActionsByKey.ContainsKey(normalized))
            {
                _keybindActionsByKey[normalized] = action.Id;
            }

            var command = ResolveCommandForAction(action);
            if (!string.IsNullOrWhiteSpace(command))
            {
                _resolvedKeyCommands[action.Id] = command;
            }
        }
    }

    private string ResolveBindingForAction(KeybindAction action)
    {
        var binding = GetBindingFromScope(_config.KeyBindings, action.Id);
        if (binding == null)
        {
            binding = action.DefaultBinding;
        }

        var connectionOverride = _activeConnection?.Settings.KeyBindings;
        if (connectionOverride != null)
        {
            binding = GetBindingFromScope(connectionOverride, action.Id) ?? binding;
        }

        if (!string.IsNullOrWhiteSpace(_activeCharacterName) &&
            _activeConnection != null &&
            _activeConnection.CharacterSettings.TryGetValue(_activeCharacterName, out var characterSettings) &&
            characterSettings.KeyBindings != null)
        {
            binding = GetBindingFromScope(characterSettings.KeyBindings, action.Id) ?? binding;
        }

        return binding ?? string.Empty;
    }

    private string ResolveCommandForAction(KeybindAction action)
    {
        var command = GetCommandFromScope(_config.KeyBindings, action.Id);
        if (command == null)
        {
            command = action.DefaultCommand;
        }

        var connectionOverride = _activeConnection?.Settings.KeyBindings;
        if (connectionOverride != null)
        {
            command = GetCommandFromScope(connectionOverride, action.Id) ?? command;
        }

        if (!string.IsNullOrWhiteSpace(_activeCharacterName) &&
            _activeConnection != null &&
            _activeConnection.CharacterSettings.TryGetValue(_activeCharacterName, out var characterSettings) &&
            characterSettings.KeyBindings != null)
        {
            command = GetCommandFromScope(characterSettings.KeyBindings, action.Id) ?? command;
        }

        return command ?? string.Empty;
    }

    private static string? GetBindingFromScope(KeybindSettings scope, string actionId)
    {
        if (!scope.Bindings.TryGetValue(actionId, out var binding))
        {
            return null;
        }

        return string.Equals(binding, "none", StringComparison.OrdinalIgnoreCase) ? string.Empty : binding;
    }

    private static string? GetCommandFromScope(KeybindSettings scope, string actionId)
    {
        if (!scope.Commands.TryGetValue(actionId, out var command))
        {
            return null;
        }

        return string.Equals(command, "none", StringComparison.OrdinalIgnoreCase) ? string.Empty : command;
    }

    private Dictionary<string, string>? GetCharacterKeybindOverrides()
    {
        if (_activeConnection == null || string.IsNullOrWhiteSpace(_activeCharacterName))
        {
            return null;
        }

        if (!_activeConnection.CharacterSettings.TryGetValue(_activeCharacterName, out var settings) ||
            settings.KeyBindings?.Bindings == null)
        {
            return null;
        }

        return new Dictionary<string, string>(settings.KeyBindings.Bindings, StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, string>? GetCharacterKeybindCommandOverrides()
    {
        if (_activeConnection == null || string.IsNullOrWhiteSpace(_activeCharacterName))
        {
            return null;
        }

        if (!_activeConnection.CharacterSettings.TryGetValue(_activeCharacterName, out var settings) ||
            settings.KeyBindings?.Commands == null)
        {
            return null;
        }

        return new Dictionary<string, string>(settings.KeyBindings.Commands, StringComparer.OrdinalIgnoreCase);
    }

    private bool TrySaveKeybindScope(
        KeybindScope scope,
        Dictionary<string, string> bindings,
        Dictionary<string, string> commands)
    {
        if (scope == KeybindScope.Connection && _activeConnection == null)
        {
            MessageBox.ErrorQuery("Settings", "No active connection for connection-scoped keybinds.", "Ok");
            return false;
        }

        if (scope == KeybindScope.Character &&
            (_activeConnection == null || string.IsNullOrWhiteSpace(_activeCharacterName)))
        {
            MessageBox.ErrorQuery("Settings", "No active character for character-scoped keybinds.", "Ok");
            return false;
        }

        var normalizedBindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var normalizedCommands = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bindingUsage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in KeybindActions)
        {
            if (bindings.TryGetValue(action.Id, out var rawBinding))
            {
                if (!string.IsNullOrWhiteSpace(rawBinding))
                {
                    var normalized = NormalizeBindingString(rawBinding);
                    if (string.IsNullOrWhiteSpace(normalized))
                    {
                        MessageBox.ErrorQuery("Settings", $"Invalid binding for {action.Label}.", "Ok");
                        return false;
                    }

                    if (string.Equals(normalized, "none", StringComparison.OrdinalIgnoreCase))
                    {
                        normalizedBindings[action.Id] = "none";
                        continue;
                    }

                    if (bindingUsage.TryGetValue(normalized, out var conflict))
                    {
                        MessageBox.ErrorQuery("Settings", $"Binding {normalized} already used by {conflict}.", "Ok");
                        return false;
                    }

                    bindingUsage[normalized] = action.Label;
                    normalizedBindings[action.Id] = normalized;
                }
            }

            if (commands.TryGetValue(action.Id, out var rawCommand))
            {
                if (!string.IsNullOrWhiteSpace(rawCommand))
                {
                    normalizedCommands[action.Id] = rawCommand.Trim();
                }
            }
        }

        switch (scope)
        {
            case KeybindScope.Global:
                _config.KeyBindings.Bindings = normalizedBindings;
                _config.KeyBindings.Commands = normalizedCommands;
                break;
            case KeybindScope.Connection:
                _activeConnection!.Settings.KeyBindings = normalizedBindings.Count == 0 && normalizedCommands.Count == 0
                    ? null
                    : new KeybindSettings
                    {
                        Bindings = normalizedBindings,
                        Commands = normalizedCommands
                    };
                SaveConnections();
                break;
            case KeybindScope.Character:
                var settings = _activeConnection!.CharacterSettings.TryGetValue(_activeCharacterName!, out var existing)
                    ? existing
                    : new AppearanceSettings();
                settings.KeyBindings = normalizedBindings.Count == 0 && normalizedCommands.Count == 0
                    ? null
                    : new KeybindSettings
                    {
                        Bindings = normalizedBindings,
                        Commands = normalizedCommands
                    };
                _activeConnection.CharacterSettings[_activeCharacterName!] = settings;
                SaveConnections();
                break;
        }

        return true;
    }

    private static string NormalizeBindingString(string binding)
    {
        if (string.IsNullOrWhiteSpace(binding))
        {
            return string.Empty;
        }

        if (string.Equals(binding, "none", StringComparison.OrdinalIgnoreCase))
        {
            return "none";
        }

        var parts = binding.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ctrl = parts.Any(p => string.Equals(p, "ctrl", StringComparison.OrdinalIgnoreCase) || string.Equals(p, "control", StringComparison.OrdinalIgnoreCase));
        var shift = parts.Any(p => string.Equals(p, "shift", StringComparison.OrdinalIgnoreCase));
        var alt = parts.Any(p => string.Equals(p, "alt", StringComparison.OrdinalIgnoreCase) || string.Equals(p, "option", StringComparison.OrdinalIgnoreCase));

        var keyToken = parts.LastOrDefault(p =>
            !string.Equals(p, "ctrl", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(p, "control", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(p, "shift", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(p, "alt", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(p, "option", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(keyToken))
        {
            return string.Empty;
        }

        keyToken = NormalizeKeyToken(keyToken);
        return BuildBindingString(ctrl, shift, alt, keyToken);
    }

    private static string NormalizeKeyToken(string token)
    {
        if (token.Length == 1)
        {
            var ch = token[0];
            return char.IsLetter(ch) ? char.ToUpperInvariant(ch).ToString() : token;
        }

        return token.ToLowerInvariant() switch
        {
            "comma" => ",",
            "period" => ".",
            "minus" => "-",
            "equals" => "=",
            "slash" => "/",
            _ => token.ToUpperInvariant()
        };
    }

    private static KeybindSettings CloneKeybindSettings(KeybindSettings source)
    {
        return new KeybindSettings
        {
            Bindings = new Dictionary<string, string>(source.Bindings, StringComparer.OrdinalIgnoreCase),
            Commands = new Dictionary<string, string>(source.Commands, StringComparer.OrdinalIgnoreCase)
        };
    }

    public void Dispose()
    {
        _transport.Dispose();
    }
}

internal sealed record CharacterSummary(string Id, string Name, int Level, string Location);
