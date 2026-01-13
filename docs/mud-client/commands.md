# MUD Client Commands

Commands are prefixed with `/`. Slash commands are forwarded to the server as
typed (including the leading `/`), except for client-only diagnostics. Anything
else is sent as a text command using `defaultCommandType`.

## Utilities
Client utilities live in the menu bar or hotkeys (F1/F3/F5/F6/F10, etc.).

## Client Diagnostics
- `/client roster` or `/roster` prints the current proximity roster cache.

## Ability/Item Shortcuts
The server understands `/cast` (aliases: `/ability`, `/magic`), `/use` (alias: `/item`), and `/companion` (alias: `/comp`).
