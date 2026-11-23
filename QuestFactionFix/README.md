# Version 0.3.7: suppress spectator quest notifications

Spectators do not see quest notification buttons or automatic quest popups,
including public/ownerless offers restored during join/rejoin. Direct opening
of a quest-offer letter is guarded too. Existing saved notification backlogs are
hidden immediately; no manual dismissal or save edit is required. Colony viewers,
shared quest invitations, non-quest events and letter history retain their rules.

This is a local presentation filter. Pending decisions, letter IDs and synchronized
quest state remain intact, so switching into a colony restores its eligible offers.
The spectator check runs before quest-owner scans. Install the same version on
all peers and restart; no Multiplayer core or other addon update is needed.

Validation: 129 quest/addon checks pass on ARM64 and Rosetta, including the
installed ally-sharing integration and 11 spectator notification regressions.

# Version 0.3.6: cheaper quest context lookups

Quest ticks, signals and endings resolve owner and target map together rather
than independently scanning twice. Map-parent field readers are compiled and
cached by quest-part type; per-thread scratch lists/sets are reused and cleared
on exceptions and early enumeration disposal. Pools retain at most eight scratch
buffers per thread. Recruitment/explicit-field/look-target/creep-joiner precedence
is preserved, including private inherited map fields.

Resolved owners/targets are not cached across operations. Direct map-field edits,
part-list replacement, pawn movement, cleanup and faction transfers therefore
remain visible immediately. Reentrant target getters borrow independent buffers.
Saved historical ownership, public/shared quests and existing fixes remain intact.

Validation: 118 game-Mono checks on native ARM64 and Rosetta, including same-tick
retargeting, inherited fields, multi-colony classification, target ordering,
reentrant queries and exception cleanup. Synthetic mixed-part context lookup
runs were approximately one-third faster; this is not an in-game TPS measurement.
See native/TpsOptimizations.md in the source repository for benchmark limitations.
All players must install v0.3.6 and restart.

# Version 0.3.5: preserve ownership across quest cleanup

Records a known colony owner at quest addition and before End/CleanupQuestParts.
The optional questFactionFixOwner integer is saved with each quest. Ownership
queries first use live targets (including genuine multi-colony targets), then
the saved owner, accepter, and parent. UI queries never record new ownership.
Memory uses weak quest keys, so removed quests do not stay rooted by this addon.

Cleanup and later part removal can erase all evidence of a private target.
Previously those quests fell back to public notification routing. The recorded
owner now keeps their failure/outcome letters private; accepted participants
still receive shared outcomes and genuinely public quests remain public. This
also preserves historical quest visibility after target parts disappear.

The reported Paid Climate Adjuster (quest 357) has no parts or accepter in the
current save; its description names Nagi's colony. This version cannot infer
an authoritative owner for already-stripped legacy quests. It preserves owners
for newly generated quests and existing quests that still have target evidence
when their next cleanup/end runs. It does not rewrite descriptions or save files.

Validation: 110 checks in game Mono, including actual addon cleanup hooks with
a controlled destructive cleanup body, private letter delivery/archive routing,
real Scribe_Values save/load of the owner integer, explicit multi-colony override,
clearing stale single-owner data for multi-colony cleanup, accepted-ally outcome
routing after cleanup, and previous event/history/quest regressions. Full Unity
post-load and live multiplayer remain to be validated. All peers must install
v0.3.5 and fully restart before rehosting/rejoining.

# Version 0.3.4: private diplomatic incidents and letter history

Leo - Events' Scandalous insult changes goodwill against Faction.OfPlayer but
sends a world notification with no location/quest. Our generic world-letter
routing consequently broadcast it. The optional integration records the
incident's colony context, routes its letters to that colony, and adds the
colony name to the label. The original incident keeps its faction selection,
random draw and goodwill change. Map-targeted invocations use the map owner;
spectator-only invocations do not arbitrarily choose a player. Context restores
on exceptions. Other world/public events retain their existing routing.

Vanilla's common 200-entry archive limit lets short messages displace dismissed
letters. The latest save had 200 Message entries in each colony archive, with
only one ordinary letter in Dipper's archive. Culling now keeps separate
allowances of 200 eligible letters and 200 eligible other entries. Original
pinning and active-entry protections remain. This does not reconstruct already
culled letters; it preserves future letter history. This policy applies to all
archives while multifaction Multiplayer is active, independently of UI filters.

Validation: 100 game-Mono checks, including a 600-message flood, bounded letter
history, pinned/active protection, the installed Leo worker's Harmony entry point
with controlled incident body/delivery, world-tick routing, affected/other viewer,
spectator suppression, exception cleanup, public event delivery, and previous
quest/ally-sharing/marriage regressions. The fixture controls goodwill/incident
body execution; a live multiplayer session remains to be checked. Startup type
and field scan also passes with Workshop Multiplayer loaded first.

All players must install v0.3.4 and restart. No core Multiplayer replacement,
Workshop copies or save edits are included in this addon update.

# Version 0.3.3: quest offer entry points and acceptance

Guards quest acceptance buttons and pre-accept reward callbacks, MainTabWindow_Quests.Select and NewQuestLetter.OpenLetter, closing paths from event lists, archived notifications and stale UI that bypass ShouldListNow. Ordinary quest-associated letters now obey faction visibility too. Quest.Accept rejects foreign-colony/spectator UI actions and synchronized commands before Multiplayer switches to the quest map's faction. Synchronized checks use the command faction, never the local viewer. Automatic simulation acceptance outside UI/commands remains unchanged.

Invited allies and accepted participants can still view shared quests through list, selection and new-quest notification paths. Invitations use the sharing mod's separate participation controls; they do not authorize starting the owner's unaccepted quest. Custom actionable join/marriage decisions remain owner-only. Public/multi-colony quests remain visible.

Confirmed saved example: quest 360, Borcamgoto's EMI Field, targets Nagi's map parent 265 for both the EMI condition and item rewards. This release prevents future unauthorized acceptance; it does not undo an already accepted event or move existing rewards.

Validation: 86 game-Mono checks with controlled engine queries, including actual patched selection/opening/acceptance methods, command-faction versus local-viewer decisions, existing marriage/recruitment handling, and the installed ally-sharing implementation. Live multiplayer validation remains required. All players must install this version and fully restart before rehosting/rejoining.

# Version 0.3.2: diplomatic marriage offers

Recognizes custom quest letters such as Romance on the Rim's DialogLetter,
not only vanilla ChoiceLetter. Actionable offers remain in the shared active list
on every peer; drawing, bundles, tooltips and automatic opening remain private
to the owning colony. The original offer and ID are retained without cloning.

An optional integration with MenuMultiplayerFix resolves diplomatic marriage
commands from the saved offer in shared faction archives, ordered by letter ID,
rather than trusting each client's active UI list. The original handler retains
its partner checks, relations and quest signals. Consumed decisions are recorded
in a saved GameComponent so duplicate commands cannot replay an archived offer.
Invalid partner choices remain retryable; expired/historical offers are excluded.
No extra mod/API DLLs or Multiplayer core replacement are bundled.

Install the same version on both peers and fully restart. Rejoin the host's
consistent save. This prevents new divergence; it does not rewind an offer that
the host already completed or rejected, or repair a client that already diverged.

Validation: 70 checks in an isolated process using the installed game Mono and
actual RomanceOnTheRim/MenuMultiplayerFix DLLs. The real rejection handler is
exercised for owner/other colony/spectator, with a missing active UI letter;
quest ending is instrumented in the headless fixture. Also covers UI privacy,
no extra offer IDs, duplicate rejection, invalid acceptance, timeout/history,
existing quest routing and ally sharing. A live network test remains required.
The early startup scan checks addon type and field loading with Workshop MP.

Source was recovered from the installed v0.3.1 assembly because the earlier
repository source directory had been removed. Existing v0.3.1 creep-joiner and
startup compatibility behavior is retained. The regression source and build
project are now in this repository again. Historical release notes follow.

# Version 0.3.1: startup compatibility correction

Removes a compiler-generated delegate field referencing an internal Multiplayer type. Prepatcher scans addon types before Multiplayer replaces its assembly, so that field prevented startup in 0.3.0. All 0.3.0 routing and pending-offer recovery behavior remains. Early type loading was checked with the workshop Multiplayer assembly, in addition to the custom-runtime regression suite. Live startup/network validation remains required.

# Version 0.3.0: actionable offers and creep-joiner recovery

Updates the existing Quest Faction Fix package; keep the same package ID and replace the old mod on BOTH peers. Restart both games and load/rejoin the host's consistent save.

- Actionable choice letters stay in the active list on every peer. Local rendering filters the list, including tooltips and bundles, without changing the shared choice-generation input. Only the owning colony sees private offer buttons or automatic popups. Informational letter routing remains unchanged.
- Creep-joiner quests can lack mapParent parts. Ownership now falls back specifically to the saved creep-outcome pawn's map, not the viewer or spectator faction.
- Creep-joiner SetFaction quest actions targeting a player/spectator faction execute using the resolved colony owner, with the original field and faction scope restored in a finalizer.
- On load (or the first connected tick), recover existing archived creep-joiner offers only when the quest remains pending, the matching pawn is alive/spawned/unrecruited/not imprisoned, the offer has not timed out, and its signals match the saved quest outcome. Reuses the exact letter and ID, correcting its archive to the owner. Does not spawn or recruit anything. Completed/rejected quests stay closed.

For the existing Close-only dialog: after both players update, reload the host save, close any old dialog and open the restored offer from the letter stack. Recovery logs `Restored pending creep-joiner offer`. If the authoritative quest is already completed, this version intentionally does not rewind it.

Validation: 56 checks pass against the game's Mono runtime and a headless game-assembly fixture, including owner/other-colony/spectator active-list consistency, recovery idempotence, stale-offer exclusions, and spectator target/context restoration. Harmony patches are installed in that fixture. Unity rendering internal-call warnings occur in the standalone harness; actual UI rendering and network clicks still require an in-game test.

Build with `python3 build.py`. The existing publicized reference assembly is used for compilation; it is not packaged. Only the addon's DLL is distributed. A backup of v0.2.0 is in dist.

Previous documentation follows (its original repository build commands are historical).

# Multiplayer Quest Faction Fix

A standalone RimWorld 1.6 addon for the Multiplayer mod. It does not replace
Multiplayer or change its tracing implementation. All players must enable the
same addon version. Load it after Multiplayer and Multiplayer Compatibility.

## Behavior

- Resolve quest ownership from saved player-colony targets, with the accepted
  pawn's player faction as a fallback. Multiple distinct colony targets remain public. Never use the local viewer or temporary
  spectator context to assign an owner.
- Fix Multiplayer's asynchronous quest-map lookup: `Map.IsPlayerHome` is checked
  in the target colony's context, including during spectator-context loading.
- Run colony-owned quest ticks, guest join-offer creation and recruitment signals
  in the target faction's context. For guest offers, match the specific accepted
  signal to its `QuestPart_JoinPlayer` map. Restore faction context in finalizers,
  including on exceptions. Recruitment protection also applies without async time.
- Restore owner context for external quest signals and quest endings, including failures.
- Route private outcome letters to their owner and accepted shared-quest participants.
  Public quest letters and untargeted world notifications reach all colonies; letters
  with colony map targets reach those colonies. Each archive gets a distinct letter
  object/ID. Quest effects run once; only notification records are copied.
- Automatically hide quests owned by another player colony from the quest list.
  Public quests without a known player-colony owner stay visible. Vanilla quest-tab
  filters (available/active/historical, hidden and dismissed) remain in effect.

### Shared quests / Invite ally

The installed Desync Fix for Multiplayer mod's `SharedQuestListVisibility` postfix
runs after this addon's owner filter. It restores visibility for both pending
invitees and accepted participants, while preserving vanilla hidden/tab rules.
Being allied without an invitation does not expose another colony's private quest.
Sharing visibility does not change the quest's target/recruitment faction.
This interaction is verified against the actual installed sharing implementation;
there is no hard dependency on that mod. An optional reflection bridge checks
accepted participants for outcome-letter delivery. Actionable recruitment choices
remain with their owner.

This addresses the observed Banbreiioba / "Olga's Dusty Nomads" case: quest 233
targets faction 20's Gray Cadueton settlement, but its join letter was archived
under spectator faction 21. Multiplayer resolves synchronized letter actions in
the current faction's archive, explaining the inert buttons for the colony player.
Vanilla `QuestPart_JoinPlayer` then recruits to the current `Faction.OfPlayer`.

Version 0.3.0 also restores still-pending creep-joiner offers as described above. It does not undo completed recruitment decisions. No save-file schema is added.
New offers/recruitment that require a context correction write a diagnostic line
with the quest/signal and destination faction to `Player.log`.

## Install

Extract the release zip into the local RimWorld `Mods` directory, enable
**Multiplayer Quest Faction Fix**, and restart the game on every player's machine.
For this Mac installation the local directory is inside `RimWorldMac.app/Mods`.
The addon works with the existing Multiplayer build rather than shipping another
copy of Multiplayer, its API or Harmony.

## Build and verify

Build the main repository's Multiplayer client first, then:

```sh
dotnet build QuestFactionFix/Source/QuestFactionFix.csproj -c Release
dotnet build QuestFactionFix/Tests/QuestFactionFixSmoke.csproj -c Release
dotnet build QuestFactionFix/Tests/PrepareFixture/PrepareFixture.csproj -c Release
```

The addon build copies only its own DLL to `QuestFactionFix/Assemblies`.
Package `About`, `Assemblies` and this README, not the source or test dependencies.

The smoke tests use the installed game's Mono and game assembly. A headless
fixture copy disables only the graphics-loading initializers for WorldObject,
MapParent, Settlement and the quest tab; the installed game assembly is untouched.
Quest ownership, home-map checks, filtering and recruitment-signal code remain
the actual game methods. This avoids the misleading "library out of sync" warning
that results when standalone Mono invokes Unity renderer internal calls.

Prepare the fixture into the test output directory, then use the repository's
`native/tests/mono-host.c` runner:

```sh
game_managed="$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Contents/Resources/Data/Managed"
dotnet QuestFactionFix/Tests/PrepareFixture/bin/Release/net9.0/PrepareFixture.dll \
  "$game_managed/Assembly-CSharp.dll" \
  "$PWD/QuestFactionFix/Tests/bin/Release/net48/Assembly-CSharp.dll"
clang -arch x86_64 native/tests/mono-host.c -o /tmp/quest-faction-host
game_frameworks="$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Contents/Frameworks"
DYLD_LIBRARY_PATH="$game_frameworks" /tmp/quest-faction-host \
  "$game_frameworks/libmonobdwgc-2.0.dylib" "$game_managed" \
  "$PWD/QuestFactionFix/Tests/bin/Release/net48/QuestFactionFixSmoke.exe" unused
```

The 21 core checks cover real patched map lookup and quest-list methods, spectator-versus-
colony ownership, public quests, recruitment context, and restoration after normal
and exceptional exits. Actual pawn recruitment and UI clicks still need an
in-game test with a new guest offer and both a colony player and spectator online.

Pass the installed `RaidAnimalFix_Filters.dll` path as the runner's last argument
instead of `unused` to run four additional integration checks: pending invitation,
accepted participation, hidden-quest rules and uninvited nonparticipant filtering.
The headless fixture supplies the current MP viewer/connection through two helper
overrides; the sharing registry and its actual late UI postfix run unchanged.
