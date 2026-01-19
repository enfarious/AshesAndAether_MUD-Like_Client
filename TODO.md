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

Support handoff checklist (1/2/3)
1) Combat core audit
   - Locate server combat implementation (CombatManager/AbilitySystem/etc).
   - Compare behavior to .agents/Server/docs/COMBAT_SYSTEM.md.
   - Check combat events emitted: combat_start/action/hit/miss/effect/death/end.
   - Verify ATB gauge, auto-attack, cooldowns, and dangerState toggling.
   - Note any gaps and log file/line references.
2) MUD client protocol alignment
   - Switch slash input to "command" payload or raw string.
   - Implement command_response logging in MessageRouter.
   - Add proximity roster cache + apply proximity_roster_delta.
   - Ensure Nearby Entities panel uses roster channels[*].entities.
3) Movement command alignment
   - Confirm server expects /move + /stop or move payloads.
   - If /move, map ring/approach/evade to slash commands.
   - Re-test movement with heading/compass variants.
