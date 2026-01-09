# MUD Client Commands

Commands are prefixed with `/`. Anything else is sent as a text command using
`defaultCommandType`.

## Connection

- `/connect [url]`: Connect to the server (uses config if omitted).
- `/disconnect`: Graceful disconnect.
- `/handshake`: Send handshake payload.
- `/ping`: Send a ping message.

## Authentication

- `/auth`: Open the login dialog.
- `/auth guest [name]`
- `/auth token <token>`
- `/auth creds <username> <password>`

## Character

- `/select <characterId>`
- `/create <name>`

## Targeting

- `/target <name|id>`: Sets target from known entities (or raw token).
- `/clear-target`

## Utilities

- `/raw <json>`: Send raw JSON payload (advanced).
- `/raw` in `event` mode uses `event` or `type` if present.
- `/reload`: Reload `config.json`.
- `/help`
- `/quit`
