# Threat Pause Votes 0.2.0

When Multiplayer auto-pauses for a letter, only the receiving colony's connected
clients request Paused (0) speed votes. Other players retain their existing speed
votes. With Lowest Wins, the shared clock remains paused until recipients choose
a speed, without requiring unaffected players to reselect theirs.

Uses ordinary synchronized TimeSpeedVote commands, rather than changing shared
votes based on the local viewer. Requests take effect through normal server
command scheduling. The original immediate automatic pause remains in place.
An online recipient may acquire a paused vote even if their previous vote was
null; offline records and spectators never acquire blocking votes.

Uses Quest Faction Fix's recipient routing when installed, including accepted
allies and public quests. Otherwise uses colony map targets or current event
faction context. Untargeted world/public events can pause multiple receiving
colonies. Keeps the configured pause threshold: broader automatic-pause settings
also apply to qualifying non-threat letters.

Preserves Multiplayer's map/world scope: async affects the target tickable;
synchronous time covers world and all map controls. Other players' votes remain
unchanged in both modes. Disconnect, desync and ordinary manual resets retain
normal behavior. Replays, catch-up simulation and desynced clients do not emit
new pause requests. No per-tick work, save schema or core replacement is added.

Replace the old ThreatPauseVotes folder, enable Threat Pause Votes after
Multiplayer and Quest Faction Fix, and restart on all clients. Use Lowest Wins
for per-player pause votes to govern time. A synchronized clock still pauses
for everyone while any recipient holds a pause; independent colony clocks
require Multiplayer's async-time setting.

Validation: 30 checks pass using the game's Mono, actual auto-pause and scheduled
vote handlers, plus the installed quest addon's recipient routing. Covers owner
and nonowner clients, null votes, spectators/offline records, public/private
quests, async/sync scope, disconnects, replay/catch-up, and exception-safe letter
context cleanup. Live network validation remains required.
