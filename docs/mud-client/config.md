# MUD Client Config

The client reads a JSON config at startup. Default path:
`client/mud/config.json`

## Fields

- `serverUrl`: Socket.io URL (http/https or ws/wss).
- `sendMode`: `event` or `envelope` (defaults to `event`).
- `receiveMode`: `event` or `envelope` (defaults to `event`).
- `sendEventName`: Socket.io event name used in `envelope` mode.
- `receiveEventName`: Socket.io event name used in `envelope` mode.
- `protocolVersion`: Protocol version sent in the handshake.
- `clientVersion`: Client version string.
- `maxUpdateRate`: Max updates per second this client can handle.
- `autoLogin`: When true, the client will send auth automatically after connecting.
- `showDiagnosticInfo`: When true, the log keeps handshake/auth/state diagnostics and includes raw unknown events.
- `showDevNotices`: When true, show dev-only acknowledgements and raw outbound logs.
- `autoConnect`: If true, connect on startup (default: true).
- `defaultCommandType`: Message type used for typed commands (default: `command`).
  When set to `command`, the client wraps non-slash input as `/say`.
- `theme`: Theme name (`ember`, `dusk`, `terminal`, `parchment`, or `custom`).
- `customTheme`: Optional color overrides used when `theme` is `custom`.
- `chatStyle`: Optional chat log overrides (foreground/background). Fonts and text styles come from the terminal emulator.
- `combatDisplay`: Optional combat log display preferences.
- `positionCommandTemplate`: Template for ring intents.
- `rangeBands`: List of range bands for the ring widget.
- `macros`: Array of macro buttons `{ label, command }`.
- `wrapLogLines`: When true, the main log word-wraps to the available width.

## Template Tokens

- `{target}`: current target name or id
- `{self}`: player id/name
- `{angle_deg}`: 0-359 degrees (0 = north/up)
- `{range_band}`: `melee_close`, `melee_long`, `ranged_short`, `ranged_long`
- `{range_units}`: optional numeric value if the server supports it

Text input is sent as:

```json
{
  "type": "<defaultCommandType>",
  "payload": { "text": "north" }
}
```

## Messaging Modes

`event` mode emits each message as a Socket.io event:

```
socket.emit("handshake", { ...payload... })
```

`envelope` mode wraps messages into a single event name:

```
socket.emit("message", { type: "handshake", payload: { ... } })
```

## Theme Colors

The client can swap color schemes. Fonts are controlled by your terminal
emulator and are not set by the client.

Custom theme fields (all optional):

- `normalForeground`
- `normalBackground`
- `accentForeground`
- `accentBackground`
- `mutedForeground`
- `mutedBackground`

Allowed color names:
`black`, `blue`, `green`, `cyan`, `red`, `magenta`, `brown`, `gray`, `darkgray`,
`brightblue`, `brightgreen`, `brightcyan`, `brightred`, `brightmagenta`,
`brightyellow`, `white`

## Chat Style

Optional overrides for chat lines (falls back to the active theme if blank).

- `foreground`
- `background`

Uses the same color names listed in Theme Colors.

## Combat Display

Optional overrides for combat log formatting.

- `style`: `compact`, `tagged`, or `split`.
  - `compact`: one-line combat entries with optional FX suffixes.
  - `tagged`: adds `[CRIT][PEN]`-style tags for colorblind-friendly scanning.
  - `split`: main line + separate FX/impact lines.
- `showFx`: When true, include dev-only floatText details.
- `showImpactFx`: When true, include target floatText details when you are hit.
- `useColorKeys`: When true, apply color keys from combat events to the log output.
- `colorKeys`: Mapping of server color keys (ex: `player.color.hit`) to terminal color names.

Color values use the same named colors as Theme Colors.
You can edit combat color keys in the Settings dialog.
