# MUD Client Controls

## Global

- `F1`: Help
- `F2`: Toggle timestamps
- `F3`: Login dialog
- `F5`: Connect
- `F6`: Disconnect
- `F10`: Quit
- `/` or `Space`: Focus the input bar when the main window is active

## Input

- `Enter`: Send command
- `Up/Down`: Command history

## Keybinds

Keybinds are configurable in Settings. Defaults:
- Ability slots: `1`-`8`
- Quick items: `9`, `0`, `-`, `=`
- Companion mode cycle: `,` and `.`

Bindings send macro commands (e.g., `/cast ability1`, `/use item1`, `/companion next`). You can include `{target}` in commands.

## Position Ring

- Mouse click: Select position intent (sends command)
- Arrow keys: Rotate angle / change band
- `Enter` or `Space`: Confirm selection

The command emitted uses `positionCommandTemplate` from `config.json`.

## Movement Grid

```
Q W E
A S D
Z X C
```

- `Q/W/E/A/D/Z/X/C`: Walk NW/N/NE/W/E/SW/S/SE.
- Double-tap: jog. Triple-tap: run.
- `S`: Stop.
