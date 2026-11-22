# Local rendering capture

Install the matching Multiplayer build on all peers and restart. No addon or
save changes are needed. This feature is off until explicitly triggered.

While playing, press Ctrl+Shift+F8 (Fn too if macOS treats F8 as a media key).
A recording label appears. Keep the game unpaused, camera stationary, and speed
unchanged for 15 seconds. Recording stops automatically; the same hotkey stops
early. Results are written locally to:

    ~/Library/Application Support/RimWorld/MpLogs/RenderProfile-*.txt

Capture on the slower player's machine as well. For an A/B comparison, use the
same map, view, speed and tracing settings, changing only resolution (or only the
suspected overlay). Save captures with descriptive filenames after recording.

The report lists architecture, resolution, UI scale, GPU name, vsync/target FPS,
map/pawn count, configured tracing mode and actual server tracing flag,
frame-time mean/median/p95, and method timing rows
identified by assembly/type. Simulation TickManagerUpdate is measured separately
from selected map/pawn drawing, overlays, flecks, windows, main buttons, and actual
mod component drawing/OnGUI overrides. Missing/skipped targets and target-limit
omissions are reported. A high-cost stage can guide a more focused follow-up;
this does not measure every individual draw method or attribute all patch time
to its patching mod (patch overhead on a root is included in that root's time).

The map table records counter advances and achieved TPS over the capture window,
plus accumulated elapsed time inside each map's Tick calls and milliseconds per
measured tick advance. Each map is accounted separately; total simulation time
is never divided by only the viewed map's ticks. Paused maps report zero TPS and
N/A time per tick. Counter resets invalidate time-per-tick ratios; exceptions and
calls without advancement are counted explicitly. Speed is recorded at both ends,
not continuously. New maps observed mid-capture use the full capture window for
TPS, so their rate is understated. Catch-up/replay and world/network counter deltas
help identify unusual captures. Collection is bounded to 64 maps.

These are main-thread elapsed times, including waits, not GPU timestamps or
whole-process CPU percentages. Inclusive totals overlap; exclusive time subtracts
only instrumented child calls. Worker-thread drawing is not timed. Frame data
is bounded to 8,192 samples, method targets to 160, nesting to 128. Timing itself
adds overhead, especially per-pawn calls. Other UI/simulation work and native/GPU
work can be outside the covered stages. Full method-level GPU profiling would
require separate graphics tooling.

Timing hooks are installed identically on every peer; only local collection is
toggled. Collection reads timing/graphics metadata, never game RNG or letter IDs,
and does not change time votes or tracing settings. It writes one text report
after the capture rather than per-event logs. Initialization failures roll back
the optional hooks. Disabled hooks return before reading timers. Original return
values and exceptions are preserved. The rebased custom build uses protocol 58; all peers need matching builds.

Validation: eight .NET tests cover tick normalization, pauses, counter resets,
exceptions and zero-duration rates, plus nested/exclusive accounting,
recursion/overflow, disabled behavior, restarts/old tokens and mismatched nesting.
Twenty-two checks pass (eleven installed-stage availability checks, eight drawing
hook checks and three map-tick hook checks) in each of RimWorld's ARM64 and x86-64 Mono
runtimes, using controlled methods: results, nested calls, exception propagation,
cleanup, worker-thread exclusion and stop behavior. These do not execute Unity
rendering or full map simulation; the map test substitutes a controlled tick body.
The first real capture also validates stage availability and hotkey/export.

An initial macOS sample of the live ARM64 game is in artifacts/render-profile-current.txt.
Most observed busy main-thread frames were unlabelled managed JIT code. The
Unity graphics worker waited in 103 of 109 observations. This small CPU sample
cannot name the responsible mod or prove GPU saturation, which is why named
stage timing is needed. Launch parameters were observed but left unchanged.
