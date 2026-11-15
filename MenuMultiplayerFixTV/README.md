## Version 1.5.1: LTS wall television recreation

Vanilla searches for a path onto the television itself before choosing a viewing seat. A wall-mounted LTS television sharing an impassable wall tile therefore cannot be selected. The additional MenuMultiplayerFix.TV.dll supplies a fallback only after vanilla returns no job, and only for LTS_WallTelevision on a blocked tile. It preserves vanilla interaction eligibility and viewing-cell selection, requires a reachable viewing cell for mobile pawns, and supports valid in-bed viewing. Equal-distance choices use stable thing IDs without adding random draws.

Install the complete folder on every peer and fully restart RimWorld. The original MenuMultiplayerFix.dll and existing features are preserved. No save conversion or mod-list change is needed. This does not make unpowered TVs, inaccessible seating or obstructed viewing positions usable.

Validated with 18 checks against the actual game job giver and Harmony under RimWorld Mono, using controlled map/eligibility queries. Live map and multiplayer validation remain required.

Build: `dotnet build Source/MenuMultiplayerFixTV.csproj -c Release`. Copy only `Assemblies/MenuMultiplayerFix.TV.dll` into the existing MenuMultiplayerFix/Assemblies folder. Do not replace the original main DLL. Tests: build Tests/WallTVSmoke.csproj and run the net48 executable using the game's Mono host with its Managed assembly directory. The fixture intercepts world queries and never starts or edits the game.
