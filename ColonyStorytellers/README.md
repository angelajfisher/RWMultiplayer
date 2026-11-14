# Colony Storytellers 0.1.0

Allows colony players to change their own faction's storyteller, difficulty
preset and custom difficulty while the game is running. Use the normal
Options -> Gameplay -> Storyteller / Modify interface. All players in one
colony share that colony's storyteller; separate colonies keep separate settings.

Install and enable this addon after Multiplayer and Multiplayer Compatibility
(and Desync Fix for Multiplayer, if present) on every peer. Fully restart and
start/rejoin the hosted game so its command-permission list is regenerated.
The existing storyteller and custom values are preserved. No Workshop files or
Multiplayer core replacement are included.

Multiplayer already stores a Storyteller per faction, but registers storyteller
selection, difficulty preset, custom difficulty and Difficulty.CopyFrom as
host-only sync controls. This addon removes that restriction only for those four
existing handlers before the handshake. It preserves their synchronization,
serialized target resolution and vanilla UI. The server stamps commands with the
issuing player's faction; the existing Storyteller serializer resolves
Find.Storyteller in that faction's execution context. Spectator-context changes
are rejected. Unrelated host-only handlers remain restricted. Single-player
continues using its normal settings; a shared-colony session still has one shared
storyteller because that session has one player colony.

Vanilla Storyteller's three-argument constructor keeps the supplied Difficulty
object by reference. Multiplayer's new-faction creation passes the current
object, so custom difficulty could be shared accidentally. The addon clones that
object for new factions, including its mutable private curve cache. A one-time
load/first-connected-tick repair splits any existing aliases without changing
current settings. Immutable Def references remain shared as intended.

The animal raid fix's faction-specific incident targeting remains in place; no
installed Workshop assembly is patched on disk. Storyteller changes still follow
the selected storyteller's normal incident schedule; the addon does not force an
immediate raid, reset quest history or create new per-user storyteller timelines.

Validation: 24 checks pass in the installed game's Mono, exercising actual server
command permissions, actual SyncField/SyncMethod handlers in both faction
contexts, custom settings, preset CopyFrom, alias repair/cache isolation,
spectator rejection and unaffected unrelated permissions. Deserialization is
controlled in the headless fixture; actual multiplayer UI/network play still
requires validation. Early type/field loading is also checked with Workshop MP.
Build: dotnet build Source/ColonyStorytellers.csproj -c Release.
