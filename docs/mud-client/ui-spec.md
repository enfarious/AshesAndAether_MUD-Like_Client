# MUD Client UI Spec (v0)

This is a fast, text-first client with optional clicky controls. Every click maps
to a text command so the client can stay simple and work with multiple servers.

## Goals

- Fast text input and scrollback with minimal UI overhead.
- Clickable controls that send the same text commands a player could type.
- Clear positioning tools for tactical combat without a 3D view.

## Layout (baseline)

- Main log: scrollback, timestamp toggle, and channel filters.
- Input line: history, autocomplete, and slash/keyword shortcuts.
- Status strip: HP/MP/Endurance, stance, target, and buffs.
- Movement strip: current speed, facing, and available directions.
- Macro bar(s): click-to-send commands (FFXI-style).
- Context panel: target actions and quick verbs based on selection.
- Position ring widget: relative positioning around the target.
- Content rating: display when entering or updating a zone.
- Movement commands: parse `Walk.N`, `Jog.NE`, `Run.045`, `Stop`.
- Theme support: switch between presets and custom palettes.

## Clicky Menus (Macro Buttons)

Macro buttons emit plain text. They can be configured with templates to avoid
hard-coding protocol details in the UI.

Template tokens (initial set):
- `{target}`: current target name or id
- `{self}`: player id/name
- `{angle_deg}`: 0-359 degrees (0 = north/up)
- `{range_band}`: `melee_close`, `melee_long`, `ranged_short`, `ranged_long`
- `{range_units}`: optional numeric value if the server supports it

Example macros:
- `attack {target}`
- `cast "Drain" {target}`
- `position {target} {range_band} {angle_deg}`

## Position Ring Widget

The ring is a wide, clickable band around a center dot (the current target).
Clicking selects a relative position where the player will attempt to stand and
maintain distance.

### Visual Model

- Center dot: target.
- Ring: 4 concentric bands, same angles, different ranges.
- Optional overlay: north/up marker and current facing.

Default range bands (configurable):
- `melee_close`
- `melee_long` (polearms)
- `ranged_short`
- `ranged_long`

### Interaction Rules

- Click point -> polar coordinates around the center.
- Angle is continuous (not snapped), unless server requires snapping.
- Band is determined by radius (which band the click falls into).

Output event (internal intent):
```
{
  "type": "position_intent",
  "target_id": "<id>",
  "angle_deg": 0-359,
  "range_band": "melee_close|melee_long|ranged_short|ranged_long"
}
```

This intent is converted to a text command via the macro template system.
If no target is selected, the widget is disabled.

### Pathing Notes

The server handles blockers and pathfinding. The client sends intent; the server
attempts to get as close as possible and reports final position.

## Keyboard Parity

All click actions must be reachable with keyboard shortcuts. The ring should
support arrow keys or numpad to rotate an angle cursor and cycle range bands.
