# Dryad Healing Defaults

RimWorld 1.6 addon. Requires Harmony and Ideology for dryads. Load after Multiplayer
and Multiplayer Compatibility when using them; every player needs the same addon.

New dryads default to creating healing pods. Existing dryads are enabled once on
first loading with the addon. A saved migration flag preserves later manual choices,
including choosing false. Vanilla mind-state resets no longer erase the preference.
The existing Multiplayer synchronization for the vanilla toggle is retained.
No per-tick scan, sampling, or stack tracing is added.

Install the folder containing About and Assemblies in RimWorld's Mods folder,
enable it, then restart. Build with `dotnet build Source/DryadHealingFix.csproj -c Release`.
