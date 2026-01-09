# MUD Client Config

The client reads a JSON config at startup. Default path:
`clients/mud/config.json`

## Fields

- `serverUrl`: Socket.io URL (http/https or ws/wss).
- `sendMode`: `event` or `envelope` (defaults to `event`).
- `receiveMode`: `event` or `envelope` (defaults to `event`).
- `sendEventName`: Socket.io event name used in `envelope` mode.
- `receiveEventName`: Socket.io event name used in `envelope` mode.
- `protocolVersion`: Protocol version sent in the handshake.
- `clientVersion`: Client version string.
- `maxUpdateRate`: Max updates per second this client can handle.
- `autoConnect`: If true, connect on startup.
- `defaultCommandType`: Message type used for typed commands (default: `command`).
- `theme`: Theme name (`ember`, `dusk`, `terminal`, `parchment`, or `custom`).
- `customTheme`: Optional color overrides used when `theme` is `custom`.
- `positionCommandTemplate`: Template for ring intents.
- `rangeBands`: List of range bands for the ring widget.
- `macros`: Array of macro buttons `{ label, command }`.

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
