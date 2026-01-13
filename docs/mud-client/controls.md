# MUD Client Controls

## Global

- `F1`: Help
- `F2`: Toggle timestamps
- `F3`: Login dialog
- `F5`: Connect
- `F6`: Disconnect
- `F10`: Quit

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
