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
map/pawn count, tracing mode, frame-time mean/median/p95, and method timing rows
identified by assembly/type. Simulation TickManagerUpdate is measured separately
from selected map/pawn drawing, overlays, flecks, windows, main buttons, and actual
mod component drawing/OnGUI overrides. Missing/skipped targets and target-limit
omissions are reported. A high-cost stage can guide a more focused follow-up;
this does not measure every individual draw method or attribute all patch time
to its patching mod (patch overhead on a root is included in that root's time).

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
values and exceptions are preserved. The protocol remains 57.

Validation: six .NET accumulator tests cover nested/exclusive accounting,
recursion/overflow, disabled behavior, restarts/old tokens and mismatched nesting.
Eight actual Harmony hook checks pass in each of RimWorld's ARM64 and x86-64 Mono
runtimes, using controlled methods: results, nested calls, exception propagation,
cleanup, worker-thread exclusion and stop behavior. These do not execute Unity
rendering; first real capture also validates stage availability and hotkey/export.

An initial macOS sample of the live ARM64 game is in artifacts/render-profile-current.txt.
Most observed busy main-thread frames were unlabelled managed JIT code. The
Unity graphics worker waited in 103 of 109 observations. This small CPU sample
cannot name the responsible mod or prove GPU saturation, which is why named
stage timing is needed. Launch parameters were observed but left unchanged.
