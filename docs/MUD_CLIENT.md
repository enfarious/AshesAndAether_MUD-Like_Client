# MUD Client (Text)

## Overview

Terminal.Gui-based text client for Ashes & Aether. Acts as a thin UI over the
Socket.io protocol and forwards most slash commands directly to the server.

## Current Capabilities

- Connect/login flows (guest/token/creds) and character select/create.
- World entry rendering with zone description, exits, and nearby entities.
- Movement ring for directional commands and range bands.
- Proximity roster cache with full roster + delta updates.
- Nearby entity list with engage toggle; engaged entities float to the top and
  remain selected across refreshes.
- Client diagnostics: `/client roster` or `/roster`.

## Input & Keybinds

- Global key routing focuses the input field for `/`, letters, and space (when
  the main window has focus).
- Keybinds are configurable in Settings, with press-to-bind and Esc to cancel.
- Scope precedence: global, then connection overrides, then character overrides.
- Bindings and commands inherit unless explicitly set; use `none` to disable.
- Commands are macro templates and can include `{target}`.

Default bindings:
- Ability slots: `1`-`8`
- Quick items: `9`, `0`, `-`, `=`
- Companion cycle: `,` and `.`

Default commands (editable):
- `/cast ability1` ... `/cast ability8`
- `/use item1` ... `/use item4`
- `/companion prev` / `/companion next`

Server aliases expected:
- `/cast` aliases: `/ability`, `/magic`
- `/use` alias: `/item`
- `/companion` alias: `/comp`

## Proximity Roster

- Handles `proximity_roster` and `proximity_roster_delta`.
- Deltas update the cached roster; samples and lastSpeaker clear properly when
  omitted or null in payloads.
- The roster cache is used for navigation targeting and list display.

## Configuration & Profiles

- Global config: `clients/mud/config.json`.
- Connection/character-specific overrides saved in `clients/mud/connections.json`.
- Connection overrides can set auto-login on/off per connection.
- Keybinds live in settings, with optional overrides per connection/character.
