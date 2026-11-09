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

The addon prevents subsequent misrouting. It does not rewrite historical archives,
reopen old offers or undo completed recruitment decisions. Already misfiled offers
may still need a one-time dev-mode correction. No save-file schema is added.
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
