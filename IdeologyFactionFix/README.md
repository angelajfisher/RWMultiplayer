# Multiplayer Ideology Faction Fix 0.1.0

Install the complete IdeologyFactionFix folder in Mods. Enable it after Multiplayer
and the quest faction addon, on **every** player machine, then fully restart.
Requires Ideology and matching Multiplayer builds. No Workshop folders are copied.

## What is separated

- A shared ideology's follower count and minor-ideology obligations use the current
  player faction. Colonists on that faction's other maps and in caravans count;
  slaves, quest lodgers, cryptosleep pawns and other factions do not.
- Each single-holder ideology role has an independent holder, activation state
  and abilities per player faction. Roles and the ideology retain their original
  identities for existing synchronized commands and ritual references. Vanilla
  activation/deactivation thresholds and leader-role exceptions are preserved.
- Multi-holder role capacity and vacancy queries use only their faction's roster.
- Pawn-facing role checks use the pawn's faction. Viewing or appointing the same
  role for another colony does not unassign the first colony's holder.
- Tree destruction is recorded per map and responsible player faction. A colonist
  visiting another player's settlement does not inherit its tree-damage stage.
  Tree cutting and extracted-tree death mood memories are restricted to the
  responsible colony/map; witnessed social opinions retain vanilla behavior.

The shared role slot and pooled follower count are confirmed in the original
code. Tree-Naturism in the examined save has Trees_Desired. Its tree-density
penalty was already map-local in vanilla; cross-map leakage of that specific
penalty was not demonstrated. The addon adds faction attribution within maps
and guards mood-history routing. It preserves damage only when the original
rules consider a player responsible, rather than counting every natural death.

## Saves and scope

Existing role holders migrate to their own faction; previously overwritten
holders cannot be reconstructed. Old tree history has no actor attribution and
is preserved only for the original map's owner. New state uses normal game/map
components and Scribe reference/deep serialization. Role abilities are saved
once, with sources resolved before post-load initialization. Old saves need no
manual edit. Already-applied memories are not erased.

This fixes the audited counts, roles and tree effects. Religion definitions,
memes, precepts, shared fluid-ideology development and ritual-specific trackers
remain shared. Arbitrary mod-defined or world statistics cannot safely become
faction-local through one global filter. Other shared trackers need individual
ownership and migration rules. Use distinct ideologies if belief definitions or
reformation itself should diverge between colonies.

This addon must remain installed on all peers while using its saved independent
roles; removing it discards the additional holders from vanilla's perspective.
Single-faction and single-player paths retain vanilla behavior.

## Performance and validation

Role scopes use a bounded thread-local pool of 32 objects, clear game references
on return, and avoid Harmony argument arrays. Stable/empty role rosters reuse
snapshots. Faction state uses dictionary lookups. Read-only UI lookups do not add
persistent save state. Per-tick activity visits relevant colony factions, not
all NPC ideologies' followers; no reflection discovery runs in those paths.
Follower counts read the existing all-pawn list directly to stay correct after
same-tick conversion, recruitment or guest-status changes. Tree-history cleanup
runs every 2,500 map ticks using vanilla's 900,000-tick expiration window.

40 regression checks pass on both RimWorld ARM64 and x86-64/Rosetta Mono. They
cover pooled 2+1 versus local 3 followers, independent role appointments and
abilities, delayed enumeration, unassignment, real Harmony exception cleanup,
map/faction tree counters, mood routing, real Scribe cross-reference resolution
and post-load initialization, deterministic save order, shared role-definition removal, and vanilla fallbacks.

The fixture substitutes an explicit pawn population, disables graphics-only
initializers in a separate test assembly copy, and skips UI notifications and
pawn qualification internals. It executes actual role assignment/activity,
counting, save/load, tree-cut and thought-stage code and the production hooks.
It does not execute a live multiplayer colony or prove every third-party mod's
compatibility. Save tests register their external world objects as existing
references, matching objects ordinarily serialized elsewhere in a full save.

A warm 100,000 assigned-check/roster lookup-pair benchmark took about 34–38 ms with
zero generation-0 collections on each runtime. Unity Mono's allocation-byte
counter is unavailable; its reported zero is not proof of zero allocations.
These are small controlled benchmarks, not promised live TPS gains.

The other custom addons were reviewed: dryad preferences and threat pauses are
event-driven; quest recovery and storyteller repairs run once after load; quest
context already batches scans and caches field accessors. Their existing bounded
trace pool and quest-context benchmark were rechecked. No speculative behavior
changes or new pawn/job polling were added to those addons. TV code is unchanged.
