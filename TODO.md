WorldOfDarkness MUD Client parity TODO (protocol updates)

High priority (blocking)
- Switch outgoing slash input to command protocol payloads:
  - Emit event "command" with payload { command: "/say hi", timestamp } or raw string.
  - Keep default typed text (non-slash) consistent with server expectations.
- Add command_response handling in MessageRouter so server feedback appears in the log.

Proximity + spatial navigation
- Add proximity roster cache and handle "proximity_roster_delta" updates.
- Feed Nearby Entities panel from roster channels[*].entities bearing/elevation/range.
- Maintain sample/lastSpeaker for social context (if needed later).

Communication system
- Handle incoming "communication" events:
  - Render [SAY]/[SHOUT]/[EMOTE]/[CFH] with senderName + distance.
- Add "get_nearby" request support if server requires explicit roster fetch.

Movement
- Confirm whether server expects slash commands (/move, /stop) instead of move payloads.
- If yes, map movement UI (ring/approach/evade) to slash commands, not move events.

General protocol cleanup
- Update docs (config/commands/readme) to match new command + roster flow.
- Validate timestamp units (ms) for command/communication/move events.

Notes
- Server now sends proximity_roster_delta instead of proximity_roster.
- Proximity channels include entities[] always; sample[] only when count <= 3.
