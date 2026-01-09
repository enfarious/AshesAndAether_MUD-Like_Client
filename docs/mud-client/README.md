# MUD Client v0.1

A fast, text-first client built with .NET 8, Terminal.Gui, and SocketIOClient.
It targets the World of Darkness protocol while staying thin enough to talk to
other MUD-style servers later.

## Quick Start

```powershell
dotnet run --project clients/mud
```

Optional: pass a custom config path.

```powershell
dotnet run --project clients/mud -- .\clients\mud\config.json
```

## Features (v0.1)

- Scrollback log with optional timestamps.
- Input line with history (up/down).
- Clickable macro buttons that emit text commands.
- Position ring widget for relative movement intent.
- WebSocket (Socket.io) handshake + auth flow support.
- Login and character selection dialogs.
- Content rating display from zone data.
- Movement command parsing (`Walk.N`, `Run.045`) into move messages.
- Theme switching with presets and custom palettes.

## Protocol Modes

The client supports both Socket.io styles:

- `event`: one event per message type (matches `test-client.js`).
- `envelope`: a single event name with `{ type, payload }` messages.

## Related Docs

- `docs/mud-client/ui-spec.md`
- `docs/mud-client/config.md`
- `docs/mud-client/commands.md`
- `docs/mud-client/controls.md`
- `docs/mud-client/themes.md`
