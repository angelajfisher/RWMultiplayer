# Threat Pause Votes 0.1.0

RimWorld 1.6 Multiplayer addon. When a letter triggers the configured automatic
pause, replaces the clearing of existing speed votes with Paused (0). With Lowest
Wins time control, one player speeding up cannot release another player's pause.
Each voter must select a speed when ready. Existing null/abstaining votes remain
null; disconnected player records cannot acquire an unreleasable pause vote.

Preserves Multiplayer's configured auto-pause threshold and map/world scope:
async time affects the target tickable; synchronous time affects all tickables.
It applies to all letters that meet that threshold, including non-threat letters
if the host has selected a broader setting. Ordinary manual resets, disconnects,
desync resets, and non-pausing letters are unchanged. No extra network commands,
per-tick work, save schema or Multiplayer core replacement are needed.

Install the folder containing About and Assemblies in local Mods and enable
Threat Pause Votes after Multiplayer. All players need the same addon and must
restart. Use Lowest Wins time control for per-player pause votes to govern speed.

Validation: 14 checks pass using the installed game's Mono and the actual
Multiplayer PauseOnLetter implementation, including both async and sync branches,
Lowest Wins aggregation, a second player's retained pause, null/offline records,
and unchanged ordinary disconnect/manual reset behavior. Live network play
remains to be verified. The Harmony patch validates exactly two replaced calls
and fails visibly if Multiplayer's implementation changes.
