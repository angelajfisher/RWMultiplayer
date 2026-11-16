# Expanded, deduplicated desync history

Retains the first 4,096 and latest 32,768 captured events per sync opinion
(36,864 total, versus the previous 2,304). Capture frequency and the existing
full-first-4,096-then-power-of-two burst sampling are unchanged.

Repeated raw stacks share immutable frame arrays within an opinion, using full
frame equality, not a stack hash. Each event keeps its own tick, event number,
RNG state, hash and thing/faction context. Capture objects return immediately to
their pool. Eviction/clear releases frame definitions once no retained event
uses them. Arrays copied from the capture pool cannot change retained history.
This reduces per-event frame storage but event metadata and unique stacks still
consume memory; the larger bounded history uses more RAM than the old ring.

Report events reference Stack:N and each stack definition appears once. Read
those references when comparing events; a reference is not a skipped capture.
The first retained mismatch also includes its full stack directly. Reports still
mark the middle eviction gap and text truncation explicitly. Reports are capped
at approximately 8 Mi characters / 32 MiB UTF-8, and compressed fields at 8 MiB.

Routine hash packets still contain the original first-256/latest-2,048 hash
window and all-capture rolling digest. No extra routine network bandwidth or
protocol change (57). A digest mismatch outside that small window does not mean
the expanded local stack history is missing: compare the exported event logs.
Both peers should use this build for expanded reports. Zstandard compression,
fragmentation on both transfer legs, and payload bounds remain unchanged.

Validation: 79 targeted .NET checks for trace history, structural interning,
hash collisions, lifecycle, sampling, compression and packet bounds; 21 checks
under the game's Mono runtime for retained raw-event metadata, immutable frame
sharing, shared report definitions, 40,000-event wrap/export, production host /
server / joiner relay with actual fragment reassembly, and existing real reports.
Live multiplayer validation remains required.
